# DependencyGraph CLI

A cross-platform command-line tool for inspecting DGML dependency graphs produced by:
- NativeAOT (`--dgmllog` / `<IlcGenerateDgmlFile>true</IlcGenerateDgmlFile>`)
- Crossgen2 (`--dgmllog`)
- IL Linker (`--dump-dependencies --dependencies-file-format dgml`)

The tool shares its core logic (graph model + DGML parsing) with the Windows-only WinForms viewer but has no UI or ETW dependencies.

## Build

```
dotnet build runtime2/src/coreclr/tools/aot/DependencyGraphCli
```

## Basic Usage

```
dotnet run --project runtime2/src/coreclr/tools/aot/DependencyGraphCli -- <path-to-graph.dgml[.xml]> [options]
```

You can also pipe a DGML file through stdin:

```
cat graph.dgml.xml | dotnet run --project runtime2/src/coreclr/tools/aot/DependencyGraphCli -- - --summary
```

## Options

General:
- `-h`, `--help`            Show help.
- `--summary`               Print graph summary (default when no other actions specified).
- `--ui`                    Launch an interactive ImGui-based viewer (requires a GPU with OpenGL 3.3 support).
- `--list`                  List all nodes (optionally filtered).
- `--filter <regex>`        Filter nodes using a regular expression applied to `Index: <id>, Name: <name>`.

Node-centric (requires `--node <id|exact-name>`):
- `--node <id|name>`        Select a node by numeric ID or exact name.
- `--show`                  Display basic node info.
- `--deps`                  Show incoming (sources / dependees) and outgoing (targets / dependents).
- `--reasons`               Show edge reasons grouped by incoming and outgoing edges.
- `--path-to-root`          Show one BFS path from the node to a root (a node with no incoming edges).
- `--json`                  Emit a JSON representation of the node (including reasons).

If `--node` is provided without any action flags, `--show --deps --reasons` are implied.

## Examples

Summary only:
```
dependencygraph mygraph.dgml.xml --summary
```

List nodes matching regex:
```
dependencygraph mygraph.dgml.xml --list --filter "System\."
```

Inspect a node by ID:
```
dependencygraph mygraph.dgml.xml --node 42 --show --deps --path-to-root
```

Inspect a node by exact name and dump edge reasons:
```
dependencygraph mygraph.dgml.xml --node "System.String" --reasons
```

JSON output for a node:
```
dependencygraph mygraph.dgml.xml --node 17 --json
```

## ImGui Viewer

Pass `--ui` to open a cross-platform viewer powered by [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET). The window shows the parsed graphs on the left, a tree view of nodes (including incoming/outgoing edges and reasons) in the center, and detailed information for the selected node on the right.

The viewer requires a GPU capable of OpenGL 3.3 or later. Keyboard and mouse navigation follow Dear ImGui conventions (e.g., Tab to move focus, mouse wheel to scroll). Close the window to return to the command line.

## Exit Codes

- 0  Success
- 2  Invalid arguments
- 3  File I/O error
- 4  DGML parse error
- 5  Node not found

## Notes

- Conditional edges are represented identically to the viewer (synthetic "Conditional(...)" nodes).
- Multiple links between the same pair of nodes are aggregated; all reasons are preserved.
- Empty `Reason` attributes show as `(no reason)` in text output.
- This tool intentionally excludes ETW live graph ingestion to remain OS-agnostic.

## License

MIT (same as the .NET runtime repository).