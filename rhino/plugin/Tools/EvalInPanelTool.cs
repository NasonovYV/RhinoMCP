using RhMcp.Internal;

namespace RhMcp.Tools;

[McpServerToolType]
public static class EvalInPanelTool
{
    [McpServerTool("eval_in_panel", "Eval JavaScript in Panel", false, false)]
    // PanelWebViews marshals its own UI work via InvokeAndWait and blocks on tasks
    // that complete through the UI dispatcher — running this ON the UI thread deadlocks.
    [BackgroundThread]
    [Description("Run JavaScript inside a Blazor Hybrid Rhino panel's WebView2 and return the " +
        "JSON-encoded result. One tool for panel assertions AND interaction: read text " +
        "(document.body.innerText), query the DOM, click buttons (el.click()), fill inputs " +
        "(set value, then dispatch new Event('input',{bubbles:true}) so Blazor sees it). " +
        "The panel is opened/selected first. Only synchronous results return — a promise " +
        "comes back as {}. Discover panel GUIDs with list_panels.")]
    public static string EvalInPanel(
        [Description("Panel class GUID, e.g. 7F1A3B2C-5D4E-6F78-9A0B-1C2D3E4F5A6B")] string panelId,
        [Description("JavaScript expression or IIFE; result is JSON-encoded by WebView2")] string script,
        [Description("Max milliseconds to wait (default 10000)")] int timeoutMs = 10_000)
    {
        if (!Guid.TryParse(panelId, out Guid id))
            throw new ArgumentException($"Not a GUID: {panelId}");

        PanelWebViews.WebViewHandle handle = PanelWebViews.Resolve(id, timeoutMs);
        return PanelWebViews.Eval(handle.Core, script, timeoutMs);
    }
}
