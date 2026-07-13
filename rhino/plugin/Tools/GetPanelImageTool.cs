using RhMcp.Internal;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetPanelImageTool
{
    [McpServerTool("get_panel_image", "Capture Panel Image", false, false)]
    // PanelWebViews marshals its own UI work via InvokeAndWait and blocks on tasks
    // that complete through the UI dispatcher — running this ON the UI thread deadlocks.
    [BackgroundThread]
    [Description("Capture a Rhino panel's web content (Blazor Hybrid panels) as PNG by panel GUID. " +
        "Opens/selects the panel first — Rhino constructs background tabs lazily. " +
        "Prefer eval_in_panel with document.body.innerText for cheap text assertions; " +
        "reach for pixels when layout/theme is the question. Discover panel GUIDs with list_panels.")]
    public static IEnumerable<ContentBlock> GetPanelImage(
        [Description("Panel class GUID, e.g. 7F1A3B2C-5D4E-6F78-9A0B-1C2D3E4F5A6B")] string panelId,
        [Description("Max milliseconds to wait for the panel's WebView (default 10000)")] int timeoutMs = 10_000)
    {
        if (!Guid.TryParse(panelId, out Guid id))
            throw new ArgumentException($"Not a GUID: {panelId}");

        PanelWebViews.WebViewHandle handle = PanelWebViews.Resolve(id, timeoutMs);
        byte[] png = PanelWebViews.CapturePng(handle, timeoutMs);

        return
        [
            ContentBlock.CreateText(JsonSerializer.Serialize(new { panelId = id, pngBytes = png.Length })),
            ContentBlock.CreateImage(png, "image/png"),
        ];
    }
}
