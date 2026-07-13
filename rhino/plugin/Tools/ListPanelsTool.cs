using System.Reflection;

using Rhino.UI;

namespace RhMcp.Tools;

[McpServerToolType]
public static class ListPanelsTool
{
    [McpServerTool("list_panels", "List Panels", true, false)]
    [Description("List Rhino panels registered by loaded plugins: panel GUID (feed it to " +
        "eval_in_panel / get_panel_image), caption, hosting .NET type, owning plugin, and " +
        "open/visible state. A plugin's panels register when the plugin loads — if one is " +
        "missing, load the plugin first (run_csharp: Rhino.PlugIns.PlugIn.LoadPlugIn).")]
    public static string ListPanels()
    {
        Guid[] open = Panels.GetOpenPanelIds();
        Dictionary<Guid, string> plugins;
        try { plugins = Rhino.PlugIns.PlugIn.GetInstalledPlugIns(); }
        catch { plugins = new Dictionary<Guid, string>(); }

        // Registered panels live in the internal Rhino.UI.PanelSystem.Definitions
        // (static Dictionary<Guid, PanelDefinition>). Reflection into Rhino internals is
        // acceptable for this dev harness; when the internals move in a future Rhino the
        // tool degrades to open-panels-only rather than breaking.
        var rows = new List<Dictionary<string, object?>>();
        IDictionary? defs = ReadDefinitions();
        if (defs is not null)
        {
            foreach (DictionaryEntry entry in defs)
            {
                Guid id = (Guid)entry.Key;
                object def = entry.Value!;
                Guid plugInId = Prop(def, "PlugInId") as Guid? ?? Guid.Empty;
                rows.Add(new Dictionary<string, object?>
                {
                    ["panelId"] = id,
                    ["caption"] = Prop(def, "EnglishCaption") as string,
                    ["type"] = (Prop(def, "Type") as Type)?.FullName,
                    ["plugIn"] = plugins.TryGetValue(plugInId, out string? name) ? name : plugInId.ToString(),
                    ["open"] = open.Contains(id),
                    ["visible"] = Panels.IsPanelVisible(id),
                });
            }
        }
        else
        {
            foreach (Guid id in open)
                rows.Add(new Dictionary<string, object?>
                {
                    ["panelId"] = id,
                    ["open"] = true,
                    ["visible"] = Panels.IsPanelVisible(id),
                });
        }

        return JsonSerializer.Serialize(new
        {
            registry = defs is not null ? "full" : "openPanelsOnly",
            panels = rows
                .OrderBy(r => r.GetValueOrDefault("plugIn") as string)
                .ThenBy(r => r.GetValueOrDefault("caption") as string),
        });
    }

    private static IDictionary? ReadDefinitions() =>
        typeof(Panels).Assembly.GetType("Rhino.UI.PanelSystem")
            ?.GetProperty("Definitions", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null) as IDictionary;

    private static object? Prop(object target, string name)
    {
        try { return target.GetType().GetProperty(name)?.GetValue(target); }
        catch { return null; }
    }
}
