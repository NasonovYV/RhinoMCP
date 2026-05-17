# Testing Rhino Plugins via MCP

## How It Works

The RhinoMCP router launches Rhino 9 WIP automatically and exposes tools (run C#, run commands, query objects, capture viewport). You test plugins by:
1. Building the plugin
2. Loading it into Rhino via a C# script
3. Calling its methods via reflection
4. Inspecting return values and exceptions

No manual Rhino interaction needed.

## Setup for Any Plugin Project

Add this `.mcp.json` to the plugin project root:
```json
{
  "mcpServers": {
    "rhino-mcp-router-dev": {
      "type": "stdio",
      "command": "D:\\Tech\\RhinoMCP2\\rhino\\router\\bin\\Debug\\net8.0\\win-x64\\publish\\rhino-mcp-router.exe",
      "args": ["--default-version", "WIP"],
      "env": {
        "RHINO_PACKAGE_DIRS": "D:\\Tech\\RhinoMCP2\\rhino\\plugin\\bin\\Debug"
      }
    }
  }
}
```

Then open a Claude Code chat from that plugin's folder. No access to `D:\Tech\RhinoMCP2` is needed — the MCP tools are available via the router binary.

## Rebuild the Router (only if RhinoMCP2 source changes)

```bash
cd D:\Tech\RhinoMCP2
dotnet build rhino/plugin/RhMcp.csproj
```

Requires .NET 10 SDK. The router targets Rhino 9 WIP.

---

## Testing Workflow

### Step 1: Build the plugin

```bash
dotnet build path/to/Plugin.csproj -f net8.0
```

### Step 2: Load into Rhino

Use `run_csharp` MCP tool:
```csharp
using System;
var path = @"D:\path\to\bin\Debug\net8.0\Plugin.rhp";
Guid pluginId;
var result = Rhino.PlugIns.PlugIn.LoadPlugIn(path, out pluginId);
Console.WriteLine($"Result: {result}, ID: {pluginId}");
```

### Step 3: Discover types and methods

```csharp
using System;
using System.Linq;
using System.Reflection;

var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "PluginAssemblyName");
foreach (var t in asm.GetExportedTypes())
{
    Console.WriteLine($"[{t.FullName}]");
    var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
    foreach (var m in methods)
    {
        var parms = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
        Console.WriteLine($"  {m.ReturnType.Name} {m.Name}({parms})");
    }
}
```

### Step 4: Call methods

```csharp
using System;
using System.Linq;
using System.Reflection;

var asm = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "PluginAssemblyName");
var type = asm.GetType("Namespace.ClassName");

// Static method:
var result = type.GetMethod("MethodName").Invoke(null, new object[] { arg1, arg2 });
Console.WriteLine($"Result: {result}");

// Instance method via singleton:
var instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static).GetValue(null);
var value = type.GetMethod("SomeMethod").Invoke(instance, null);
Console.WriteLine($"Value: {value}");
```

### Step 5: Catch exceptions

```csharp
using System;
using System.Reflection;

try
{
    // ... call method ...
}
catch (TargetInvocationException ex)
{
    var inner = ex.InnerException;
    Console.WriteLine($"EXCEPTION: {inner.GetType().Name}: {inner.Message}");
    Console.WriteLine($"Stack:\n{inner.StackTrace}");
}
```

### Step 6: Run Rhino commands

Use `run_command` MCP tool:
```
_-Box 0,0,0 10,10,10
```

### Step 7: Visual verification

Use `get_viewport_image` MCP tool with view/displayMode params.

---

## Available MCP Tools

| Tool | Purpose |
|------|---------|
| `run_csharp` | Execute C# in Rhino process (reflection, .NET API) |
| `run_python` | Execute Python in Rhino process |
| `run_command` | Run any Rhino command string |
| `list_objects` | Query objects by type/layer/name |
| `get_viewport_image` | Capture viewport as JPG |
| `list_slots` | See running Rhino instances |
| `spawn_slot` | Launch additional Rhino instances |

## Tips

- **Hot reload**: Plugin assemblies cache per Rhino session. To reload after rebuild: the router must spawn a fresh Rhino. Close the current one or use a new slot.
- **No UI clicking needed**: Call the methods that buttons invoke directly via reflection.
- **Any public method is testable**: Singletons, static methods, instance methods on discovered objects.
- **Strategies that need files**: Create test .3dm files via `run_csharp` using `File3dm.Write()`.
