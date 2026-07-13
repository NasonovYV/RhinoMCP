# RhinoMCP2

MCP (Model Context Protocol) server enabling AI agents to create and edit Rhino 3D models programmatically. Runs inside Rhino as a plugin, exposes ~45 tools via MCP protocol.

Fork of a McNeel repository with local modifications. Low maintenance priority — its main job here is serving as the AI test harness for plugin development. Sync upstream periodically to pick up fixes.

## What It Does

AI agents (Claude, OpenHands) send MCP tool calls → plugin executes inside Rhino → returns geometry data, viewport images, command results.

**~45 tools:** geometry queries, viewport capture, Rhino commands, C#/Python scripting, panel UI testing (Blazor Hybrid panels — local fork addition), Grasshopper 1/2 component management and solving.

## Tech Stack

| Component | Technology |
|-----------|-----------|
| Plugin | C# .NET 8.0, Rhino SDK 8.29+, Grasshopper 8.29+ |
| Protocol | MCP (stdio-based), ModelContextProtocol 1.2.0 |
| Router | .NET 8.0 CLI (spawns/manages multiple Rhino instances) |
| Distribution | .yak package (Rhino Package Manager) |
| Platforms | Windows (framework-dependent) + macOS (NativeAOT) |

## Project Structure

```
RhinoMCP2/
├── rhino/
│   ├── plugin/                  Rhino .rhp plugin
│   │   ├── McpServer.cs         MCP server initialization
│   │   ├── Plugin.cs            Plugin entry point
│   │   └── Tools/ (25 classes)  MCP tool implementations
│   └── router/                  CLI app for multi-instance management
│       └── codegen/             Source generator for router proxy tools
├── cc-plugin/                   Claude Code plugin configuration
└── connector/                   MCP connector for Claude Desktop (.mcpb)
```

## Tool Categories

| Category | Tools | Purpose |
|----------|-------|---------|
| Geometry | GetSelection, ListObjects, SetSelection | Query/select objects |
| Viewport | GetViewportImage, SetCamera, ZoomToLayer/Object | Visual inspection |
| Commands | RunCommand, GetCommands | Execute Rhino commands |
| Scripting | RunCSharp, RunPython | Execute code inside Rhino |
| Documents | OpenDoc, SaveDoc, CloseDoc, GetContext | Document lifecycle (upstream v2) |
| Panels (local) | ListPanels, EvalInPanel, GetPanelImage | Blazor Hybrid panel UI testing: discover panels, drive/read the DOM, capture PNG |
| Grasshopper 1 | 11 tools | Canvas management, component placement, solving |
| Grasshopper 2 | 11 tools | Canvas management, component placement, solving |

## Relationship to Other Projects

| Project | Relationship |
|---------|-------------|
| **buildeer** | Can expose buildeer smart objects and recipes to AI agents |
| **OpenHands** | Configured as MCP server in OpenHands Docker (port 10500) |
| **LifeOS** | Parallel MCP effort — same pattern, different domain |
| **BuildeerUniCore** | Math library could be exposed via tools for geometric computation |

## Reuse & Sharing Opportunities

- **MCP server pattern** is reusable for any application needing AI agent integration
- **Router architecture** (spawn/manage multiple instances) applicable to other heavy desktop apps
- **Source generator** for proxy tools could be generalized

## Notes

- Plugin loaded via PackageManager in Rhino
- ASP.NET Core 8.0 framework bundled in plugin output (avoids system .NET dependency)
- Stale slot error after Rhino restart (auto-reconnects on retry)
- TODO: port check before startup, macOS crash reports, fallback registry-based Rhino lookup
