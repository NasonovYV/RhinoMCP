using Microsoft.Extensions.AI;

using ModelContextProtocol;

using RhMcp.Internal;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetPanelImageTool
{
    [McpServerTool(Name = "get_panel_image")]
    [Description("Capture a Rhino panel's web content (Blazor Hybrid panels) as PNG by panel GUID. " +
        "Opens/selects the panel first — Rhino constructs background tabs lazily. " +
        "Prefer eval_in_panel with document.body.innerText for cheap text assertions; " +
        "reach for pixels when layout/theme is the question.")]
    public static IEnumerable<AIContent> GetPanelImage(
        [Description("Panel class GUID, e.g. 7F1A3B2C-5D4E-6F78-9A0B-1C2D3E4F5A6B")] string panelId,
        [Description("Max milliseconds to wait for the panel's WebView (default 10000)")] int timeoutMs = 10_000)
    {
        if (!Guid.TryParse(panelId, out Guid id))
            throw new McpException($"Not a GUID: {panelId}");

        object core = PanelWebViews.ResolveCore(id, timeoutMs);
        byte[] png = PanelWebViews.CapturePng(core, timeoutMs);

        return
        [
            new TextContent(JsonSerializer.Serialize(new { panelId = id, pngBytes = png.Length })),
            new DataContent(png, "image/png"),
        ];
    }
}
