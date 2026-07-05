using System.IO;
using System.Threading;
using System.Threading.Tasks;

using ModelContextProtocol;

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
    /// Resolve the CoreWebView2 of the panel with the given id. Opens the panel as the
    /// selected tab first (Rhino constructs background tabs lazily) and polls while the
    /// WebView initializes asynchronously.
    /// </summary>
    public static object ResolveCore(Guid panelId, int timeoutMs)
    {
        object? core = null;
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
                core = panel is null ? null : FindCore(panel);
            });
            if (core is not null) return core;
            Thread.Sleep(250);
        } while (Environment.TickCount64 < deadline);

        throw new McpException(
            $"No initialized WebView2 found for panel {panelId}. Either this is not a " +
            "registered panel id, the owning plugin is not loaded (load it first, e.g. " +
            "run_python: Rhino.PlugIns.PlugIn.LoadPlugIn(pluginGuid)), or the panel " +
            "hosts no browser (native Eto/WPF panel).");
    }

    /// <summary>Capture the web content as PNG. Call from a non-UI thread.</summary>
    public static byte[] CapturePng(object core, int timeoutMs)
    {
        using MemoryStream ms = new();
        Task? task = null;
        RhinoApp.InvokeAndWait(() =>
        {
            Type coreType = core.GetType();
            Type fmtType = coreType.Assembly.GetType(
                "Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat", throwOnError: true)!;
            object png = Enum.ToObject(fmtType, 0); // Png
            task = (Task)coreType.GetMethod("CapturePreviewAsync")!
                .Invoke(core, new object[] { png, ms })!;
        });
        // The capture completes via the UI thread's dispatcher — waiting THERE would
        // deadlock; waiting here (Kestrel thread) is safe.
        if (task is null || !task.Wait(timeoutMs))
            throw new McpException("Panel capture timed out — is the panel visible on screen?");
        return ms.ToArray();
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
            throw new McpException("Panel eval timed out.");
        return task.Result;
    }

    // Breadth-first walk over Content/Child/Children looking for a node that exposes a
    // CoreWebView2 (a WebView2 control) or a WebView property that does (a BlazorWebView).
    // Must run on the UI thread — WPF property getters have dispatcher affinity.
    private static object? FindCore(object root)
    {
        Queue<(object Node, int Depth)> queue = new();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            (object node, int depth) = queue.Dequeue();

            if (Prop(node, "CoreWebView2") is { } core)
                return core;
            if (Prop(node, "WebView") is { } wv && Prop(wv, "CoreWebView2") is { } core2)
                return core2;

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
