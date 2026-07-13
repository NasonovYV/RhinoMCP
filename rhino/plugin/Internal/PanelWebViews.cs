using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Rhino.UI;

namespace RhMcp.Internal;

/// <summary>
/// Reflection-only bridge to a Blazor Hybrid panel's WebView2 (Flimt panels et al).
/// Deliberately no compile-time reference to WPF or the WebView2 packages: the harness
/// must not ship any assembly the product plugins also load — first-loaded-wins in
/// Rhino's shared default ALC would couple harness and product versions.
/// </summary>
internal static class PanelWebViews
{
    /// <summary>
    /// The panel's CoreWebView2 plus (when found) its CoreWebView2Controller. The
    /// controller is only needed to force renderer visibility during capture.
    /// </summary>
    public readonly record struct WebViewHandle(object Core, object? Controller);

    /// <summary>
    /// Resolve the WebView2 of the panel with the given id. Opens the panel as the
    /// selected tab first (Rhino constructs background tabs lazily) and polls while the
    /// WebView initializes asynchronously. Also waits until the hosted page has content
    /// (a freshly created WebView sits on about:blank until Blazor's host page loads —
    /// returning then would hand callers an empty document).
    /// </summary>
    public static WebViewHandle Resolve(Guid panelId, int timeoutMs)
    {
        WebViewHandle? handle = null;
        long deadline = Environment.TickCount64 + Math.Max(1000, timeoutMs);
        bool first = true;
        do
        {
            RhinoApp.InvokeAndWait(() =>
            {
                if (first)
                {
                    Panels.OpenPanel(panelId, true);
                    first = false;
                }
                uint docSerial = RhinoDoc.ActiveDoc?.RuntimeSerialNumber ?? 0u;
                object? panel = Panels.GetPanel(panelId, docSerial);
                handle = panel is null ? null : FindHandle(panel);
            });
            if (handle is { } h && HasDocumentContent(h.Core))
                return h;
            Thread.Sleep(250);
        } while (Environment.TickCount64 < deadline);

        // WebView exists but the page never got content — better to hand the caller a
        // live handle than to claim there is no WebView.
        if (handle is { } stale) return stale;

        throw new InvalidOperationException(
            $"No initialized WebView2 found for panel {panelId}. Either this is not a " +
            "registered panel id, the owning plugin is not loaded (load it first, e.g. " +
            "run_python: Rhino.PlugIns.PlugIn.LoadPlugIn(pluginGuid)), or the panel " +
            "hosts no browser (native Eto/WPF panel).");
    }

    /// <summary>Capture the web content as PNG. Call from a non-UI thread.</summary>
    public static byte[] CapturePng(WebViewHandle handle, int timeoutMs)
    {
        // Both capture paths need the renderer producing frames, which it does not do
        // while the controller reports IsVisible=false (panel tab deselected, window
        // minimized, locked/unattended session — the normal state on a test machine).
        // Force visibility for the duration of the capture, then restore.
        bool forced = ForceControllerVisible(handle.Controller);
        try
        {
            MemoryStream ms = new();
            Task? task = null;
            RhinoApp.InvokeAndWait(() =>
            {
                Type coreType = handle.Core.GetType();
                Type fmtType = coreType.Assembly.GetType(
                    "Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat", throwOnError: true)!;
                object png = Enum.ToObject(fmtType, 0); // Png
                task = (Task)coreType.GetMethod("CapturePreviewAsync")!
                    .Invoke(handle.Core, new object[] { png, ms })!;
            });
            // The capture completes via the UI thread's dispatcher — waiting THERE would
            // deadlock; waiting here (Kestrel thread) is safe.
            try
            {
                if (task is not null && task.Wait(Math.Min(timeoutMs, 10_000)))
                    return ms.ToArray();
            }
            catch (AggregateException)
            {
                // capture failed outright — fall through to the DevTools path
            }

            // Fallback: DevTools screenshot renders from the surface on demand and does
            // not require a presented frame.
            Task<string>? cdp = null;
            RhinoApp.InvokeAndWait(() =>
            {
                cdp = (Task<string>)handle.Core.GetType().GetMethod("CallDevToolsProtocolMethodAsync")!
                    .Invoke(handle.Core, new object[]
                    {
                        "Page.captureScreenshot",
                        """{"format":"png","fromSurface":true}""",
                    })!;
            });
            if (cdp is null || !cdp.Wait(timeoutMs))
                throw new InvalidOperationException("Panel capture timed out — is the panel visible on screen?");
            using JsonDocument doc = JsonDocument.Parse(cdp.Result);
            return Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!);
        }
        finally
        {
            if (forced) SetControllerVisible(handle.Controller!, false);
        }
    }

    // Sets CoreWebView2Controller.IsVisible = true if it was false; returns whether it
    // was forced (caller restores). The controller has UI-thread affinity.
    private static bool ForceControllerVisible(object? controller)
    {
        if (controller is null) return false;
        bool forced = false;
        RhinoApp.InvokeAndWait(() =>
        {
            try
            {
                PropertyInfo? vis = controller.GetType().GetProperty("IsVisible");
                if (vis?.GetValue(controller) is false)
                {
                    vis.SetValue(controller, true);
                    forced = true;
                }
            }
            catch
            {
                // controller in a bad state — capture will proceed and time out honestly
            }
        });
        return forced;
    }

    private static void SetControllerVisible(object controller, bool value)
    {
        RhinoApp.InvokeAndWait(() =>
        {
            try { controller.GetType().GetProperty("IsVisible")?.SetValue(controller, value); }
            catch { }
        });
    }

    // True once the hosted page has rendered something into <body> — filters out the
    // about:blank phase between WebView2 creation and the Blazor host page loading.
    private static bool HasDocumentContent(object core)
    {
        try
        {
            return Eval(core, "!!(document.body && document.body.firstElementChild)", 2_000) == "true";
        }
        catch
        {
            return false; // eval not possible yet — keep polling
        }
    }

    /// <summary>Run JavaScript in the panel's WebView2; returns the JSON-encoded result.</summary>
    public static string Eval(object core, string script, int timeoutMs)
    {
        Task<string>? task = null;
        RhinoApp.InvokeAndWait(() =>
        {
            task = (Task<string>)core.GetType().GetMethod("ExecuteScriptAsync")!
                .Invoke(core, new object[] { script })!;
        });
        if (task is null || !task.Wait(timeoutMs))
            throw new InvalidOperationException("Panel eval timed out.");
        return task.Result;
    }

    // Breadth-first walk over Content/Child/Children looking for a node that exposes a
    // CoreWebView2 (a WebView2 control) or a WebView property that does (a BlazorWebView).
    // The matching node is the WebView2 host control; its private controller field is
    // grabbed alongside so capture can force renderer visibility.
    // Must run on the UI thread — WPF property getters have dispatcher affinity.
    private static WebViewHandle? FindHandle(object root)
    {
        Queue<(object Node, int Depth)> queue = new();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            (object node, int depth) = queue.Dequeue();

            if (Prop(node, "CoreWebView2") is { } core)
                return new WebViewHandle(core, FindController(node));
            if (Prop(node, "WebView") is { } wv && Prop(wv, "CoreWebView2") is { } core2)
                return new WebViewHandle(core2, FindController(wv));

            if (depth >= 4)
                continue;
            if (Prop(node, "Content") is { } content && content is not string)
                queue.Enqueue((content, depth + 1));
            if (Prop(node, "Child") is { } child)
                queue.Enqueue((child, depth + 1));
            if (Prop(node, "Children") is IEnumerable children)
                foreach (object? kid in children)
                    if (kid is not null)
                        queue.Enqueue((kid, depth + 1));
        }
        return null;
    }

    // The WPF/WinForms WebView2 controls keep their CoreWebView2Controller in a private
    // field; match by field type name so the SDK's field naming can change freely.
    private static object? FindController(object webViewControl)
    {
        try
        {
            for (Type? t = webViewControl.GetType(); t is not null; t = t.BaseType)
            {
                foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                {
                    if (f.FieldType.Name.Contains("CoreWebView2") && f.FieldType.Name.Contains("Controller")
                        && f.GetValue(webViewControl) is { } controller)
                    {
                        return controller;
                    }
                }
            }
        }
        catch
        {
            // no controller — capture falls back to honest timeout behavior
        }
        return null;
    }

    private static object? Prop(object target, string name)
    {
        try
        {
            var prop = target.GetType().GetProperties()
                .FirstOrDefault(p => p.Name == name && p.GetIndexParameters().Length == 0);
            return prop?.GetValue(target);
        }
        catch
        {
            return null; // getter threw (not initialized, wrong thread guard, …) — skip
        }
    }
}
