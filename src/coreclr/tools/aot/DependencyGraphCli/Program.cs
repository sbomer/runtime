// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using DependencyGraphCli.ImGuiUi;
using DependencyGraphCore;

namespace DependencyGraphCli
{
    internal static class Program
    {
        // Exit codes (conservative; 0 = success)
        private const int EXIT_SUCCESS = 0;
        private const int EXIT_INVALID_ARGS = 2;
        // Removed unused EXIT_FILE_ERROR analyzer warning (CA1823)
        private const int EXIT_PARSE_ERROR = 4;
        private const int EXIT_NODE_NOT_FOUND = 5;

        public static int Main(string[] args)
        {
            if (args.Length == 0 || HasFlag(args, "--help") || HasFlag(args, "-h"))
            {
                PrintUsage();
                return EXIT_SUCCESS;
            }

            // First non-option argument is the DGML path (or "-" for stdin)
            string? dgmlPath = args.FirstOrDefault(a => !a.StartsWith('-'));
            if (dgmlPath == null)
            {
                Console.Error.WriteLine("Error: Missing DGML file path.");
                PrintUsage();
                return EXIT_INVALID_ARGS;
            }

            bool uiRequested = HasFlag(args, "--ui");

            int fileId = 0; // Keep identical semantics: fileId used for PID/ID
            DgmlParseResult parseResult;

            if (dgmlPath == "-")
            {
                parseResult = DgmlParser.Parse(Console.OpenStandardInput(), fileId, "stdin");
            }
            else
            {
                parseResult = DgmlParser.ParseFromFile(dgmlPath, fileId);
            }

            if (!parseResult.Success)
            {
                Console.Error.WriteLine($"Error parsing DGML: {parseResult.ErrorMessage}");
                return EXIT_PARSE_ERROR;
            }

            Graph g = parseResult.Graph!;
            // Add graph to collection (mirrors original behavior of storing graphs globally).
            GraphCollection.Singleton.AddGraph(g);

            if (uiRequested)
            {
                if (DependencyGraphImGuiApp.TryRun(GraphCollection.Singleton, out string? uiFailure))
                {
                    return EXIT_SUCCESS;
                }

                if (!string.IsNullOrWhiteSpace(uiFailure))
                {
                    Console.Error.WriteLine($"Unable to launch ImGui viewer: {uiFailure}");
                }
                else
                {
                    Console.Error.WriteLine("Unable to launch ImGui viewer.");
                }

                Console.Error.WriteLine("Falling back to console output.");
                PrintSummary(g);
                return EXIT_SUCCESS;
            }

            // If no further options provided, default to summary.
            var optionArgs = args.Where(a => a.StartsWith('-') && !string.Equals(a, "--ui", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (optionArgs.Length == 0)
            {
                PrintSummary(g);
                return EXIT_SUCCESS;
            }

            // Filtering / listing
            string? regexFilter = GetOptionValue(args, "--filter");
            bool listRequested = HasFlag(args, "--list");
            string? nodeIdentifier = GetOptionValue(args, "--node");

            if (listRequested)
            {
                ListNodes(g, regexFilter);
            }

            if (nodeIdentifier != null)
            {
                Node? node = ResolveNode(g, nodeIdentifier);
                if (node == null)
                {
                    Console.Error.WriteLine($"Error: Node '{nodeIdentifier}' not found (by ID or exact name).");
                    return EXIT_NODE_NOT_FOUND;
                }

                bool showRequested = HasFlag(args, "--show");
                bool depsRequested = HasFlag(args, "--deps");
                bool pathRequested = HasFlag(args, "--path-to-root");
                bool reasonsRequested = HasFlag(args, "--reasons");
                bool jsonRequested = HasFlag(args, "--json");

                if (!(showRequested || depsRequested || pathRequested || reasonsRequested || jsonRequested))
                {
                    // Default behavior if --node provided but no action flags: show basic info
                    showRequested = true;
                    depsRequested = true;
                    reasonsRequested = true;
                }

                if (jsonRequested)
                {
                    PrintNodeJson(node);
                }
                else
                {
                    if (showRequested) PrintNodeBasic(node);
                    if (depsRequested) PrintNodeDependencies(node);
                    if (reasonsRequested) PrintNodeEdgeReasons(node);
                    if (pathRequested) PrintPathToRoot(node);
                }
            }

            if (!listRequested && nodeIdentifier == null)
            {
                // If options were given but none acted upon
                if (!(HasFlag(args, "--summary")))
                {
                    // Provide summary if explicitly requested or if no recognized actionable flag was found
                    PrintSummary(g);
                }
            }

            return EXIT_SUCCESS;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"
dependencygraph - Cross-platform DGML dependency graph inspector

Usage:
  dependencygraph <graph.dgml[.xml]> [options]

General Options:
  -h, --help                 Show this help text.
  --summary                  Print graph summary (default if no other options given).
    --ui                       Launch an interactive ImGui viewer.
  --list                     List all nodes (optionally filtered).
  --filter <regex>           Filter nodes by regex applied to 'Index: <id>, Name: <name>' format.

Node Options (require --node):
  --node <id|exact-name>     Select a node by numeric ID or exact Name.
  --show                     Show basic node info.
  --deps                     Show dependee and dependent counts.
  --reasons                  Show edge reasons grouped by source/target.
  --path-to-root             Show one path from the node to a root (a node with no sources).
  --json                     Emit a JSON object describing the node (reasons included).

Examples:
  dependencygraph mygraph.dgml.xml
  dependencygraph mygraph.dgml.xml --list
  dependencygraph mygraph.dgml.xml --filter ""MyType""
  dependencygraph mygraph.dgml.xml --node 42 --show --deps --path-to-root
  dependencygraph mygraph.dgml.xml --node ""System.String"" --reasons
");
        }

        private static bool HasFlag(string[] args, string flag) =>
            args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

        private static string? GetOptionValue(string[] args, string option)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], option, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        private static void PrintSummary(Graph g)
        {
            int edgeCount = g.Nodes.Sum(n => n.Value.Targets.Count);
            Console.WriteLine($"Graph Summary:");
            Console.WriteLine($"  Name: {g.Name}");
            Console.WriteLine($"  PID: {g.PID}, ID: {g.ID}");
            Console.WriteLine($"  Nodes: {g.Nodes.Count}");
            Console.WriteLine($"  Edges: {edgeCount}");
            Console.WriteLine($"  Roots (no incoming edges): {CountRoots(g)}");
        }

        private static int CountRoots(Graph g) =>
            g.Nodes.Values.Count(n => n.Sources.Count == 0);

        private static void ListNodes(Graph g, string? regexFilter)
        {
            IEnumerable<Node> nodes = g.Nodes.Values;
            if (!string.IsNullOrEmpty(regexFilter))
            {
                Regex r;
                try
                {
                    r = new Regex(regexFilter, RegexOptions.Compiled);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Invalid regex '{regexFilter}': {ex.Message}");
                    return;
                }
                nodes = nodes.Where(n => r.IsMatch(n.ToString()));
            }

            foreach (var n in nodes.OrderBy(n => n.Index))
            {
                Console.WriteLine(n.ToString());
            }
        }

        private static Node? ResolveNode(Graph g, string identifier)
        {
            if (int.TryParse(identifier, out int id))
            {
                if (g.Nodes.TryGetValue(id, out var nodeById))
                    return nodeById;
            }

            // Exact name match (preserve semantics: Name property only)
            return g.Nodes.Values.FirstOrDefault(n => string.Equals(n.Name, identifier, StringComparison.Ordinal));
        }

        private static void PrintNodeBasic(Node n)
        {
            Console.WriteLine($"Node:");
            Console.WriteLine($"  {n}");
            Console.WriteLine($"  Outgoing (Targets): {n.Targets.Count}");
            Console.WriteLine($"  Incoming (Sources): {n.Sources.Count}");
        }

        private static void PrintNodeDependencies(Node n)
        {
            Console.WriteLine("Dependencies:");
            Console.WriteLine("  Dependees (nodes this depends on / Sources):");
            foreach (var source in n.Sources.Keys.OrderBy(k => k.Index))
            {
                Console.WriteLine($"    {source}");
            }
            Console.WriteLine("  Dependents (nodes depending on this / Targets):");
            foreach (var target in n.Targets.Keys.OrderBy(k => k.Index))
            {
                Console.WriteLine($"    {target}");
            }
        }

        private static void PrintNodeEdgeReasons(Node n)
        {
            Console.WriteLine("Edge Reasons (Incoming):");
            foreach (var kv in n.Sources.OrderBy(k => k.Key.Index))
            {
                Console.WriteLine($"  From {kv.Key}:");
                foreach (var reason in kv.Value)
                {
                    Console.WriteLine($"    - {FormatReason(reason)}");
                }
            }

            Console.WriteLine("Edge Reasons (Outgoing):");
            foreach (var kv in n.Targets.OrderBy(k => k.Key.Index))
            {
                Console.WriteLine($"  To {kv.Key}:");
                foreach (var reason in kv.Value)
                {
                    Console.WriteLine($"    - {FormatReason(reason)}");
                }
            }
        }

        private static string FormatReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "(no reason)";
            return reason;
        }

        private static void PrintPathToRoot(Node start)
        {
            // BFS over Sources (incoming edges) until a root (no sources) is found.
            var queue = new Queue<Node>();
            var visited = new HashSet<Node>();
            var predecessor = new Dictionary<Node, Node?>();

            queue.Enqueue(start);
            visited.Add(start);
            predecessor[start] = null;

            Node? rootFound = null;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (current.Sources.Count == 0)
                {
                    rootFound = current;
                    break;
                }

                foreach (var source in current.Sources.Keys)
                {
                    if (visited.Add(source))
                    {
                        queue.Enqueue(source);
                        predecessor[source] = current;
                    }
                }
            }

            if (rootFound == null)
            {
                Console.WriteLine("No path to a root (graph may have cycles or no root definition).");
                return;
            }

            // Reconstruct path
            var path = new List<Node>();
            var cursor = rootFound;
            while (cursor != null)
            {
                path.Add(cursor);
                cursor = predecessor[cursor];
            }

            path.Reverse();

            Console.WriteLine("Path to Root:");

            // Determine the maximum index width for uniform formatting
            int maxIndex = path.Max(n => n.Index);
            int indexWidth = maxIndex.ToString().Length;

            for (int i = 0; i < path.Count; i++)
            {
                var node = path[i];
                Console.WriteLine($"[{node.Index.ToString().PadLeft(indexWidth)}] {node.Name}");
            }
        }

        private static void PrintNodeJson(Node n)
        {
            // Simple manual JSON (avoid adding dependencies)
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append($"\"index\":{n.Index},");
            sb.Append($"\"name\":{JsonEscape(n.Name)},");
            sb.Append("\"incoming\":[");
            bool firstIn = true;
            foreach (var kv in n.Sources.OrderBy(k => k.Key.Index))
            {
                if (!firstIn) sb.Append(',');
                firstIn = false;
                sb.Append('{');
                sb.Append($"\"fromIndex\":{kv.Key.Index},\"fromName\":{JsonEscape(kv.Key.Name)},\"reasons\":[");
                bool firstReason = true;
                foreach (var r in kv.Value)
                {
                    if (!firstReason) sb.Append(',');
                    firstReason = false;
                    sb.Append(JsonEscape(r));
                }
                sb.Append(']');
            }
            sb.Append(']');
            sb.Append(',');

            sb.Append("\"outgoing\":[");
            bool firstOut = true;
            foreach (var kv in n.Targets.OrderBy(k => k.Key.Index))
            {
                if (!firstOut) sb.Append(',');
                firstOut = false;
                sb.Append('{');
                sb.Append($"\"toIndex\":{kv.Key.Index},\"toName\":{JsonEscape(kv.Key.Name)},\"reasons\":[");
                bool firstReason = true;
                foreach (var r in kv.Value)
                {
                    if (!firstReason) sb.Append(',');
                    firstReason = false;
                    sb.Append(JsonEscape(r));
                }
                sb.Append('}');
            }
            sb.Append(']');

            sb.Append('}');
            Console.WriteLine(sb.ToString());
        }

        private static string JsonEscape(string s)
        {
            if (s == null) return "null";
            return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
