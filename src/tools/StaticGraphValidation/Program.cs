// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using Microsoft.Build.Locator;

if (args.Length == 0)
{
    CommandLine.PrintUsage();
    return 1;
}

string? msbuildPath = Environment.GetEnvironmentVariable("MSBUILD_GRAPH_VALIDATION_MSBUILD_PATH");
if (string.IsNullOrWhiteSpace(msbuildPath))
{
    msbuildPath = ResolveRepoMSBuildPath(Directory.GetCurrentDirectory());
}

if (string.IsNullOrWhiteSpace(msbuildPath))
{
    MSBuildLocator.RegisterDefaults();
}
else
{
    MSBuildLocator.RegisterMSBuildPath(msbuildPath);
}

return Run(args);

static string? FindDotNetRoot(string start)
{
    for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, ".dotnet");
        if (Directory.Exists(Path.Combine(candidate, "sdk")))
        {
            return candidate;
        }
    }

    return null;
}

static string? ResolveRepoMSBuildPath(string start)
{
    string? dotnetRoot = FindDotNetRoot(start);
    if (dotnetRoot is null)
    {
        return null;
    }

    string sdkRoot = Path.Combine(dotnetRoot, "sdk");
    if (!Directory.Exists(sdkRoot))
    {
        return null;
    }

    string? globalJson = FindGlobalJson(start);
    if (globalJson is not null)
    {
        using FileStream stream = File.OpenRead(globalJson);
        using JsonDocument document = JsonDocument.Parse(stream);
        if (document.RootElement.TryGetProperty("sdk", out JsonElement sdk) &&
            sdk.TryGetProperty("version", out JsonElement versionElement))
        {
            string? version = versionElement.GetString();
            if (!string.IsNullOrWhiteSpace(version))
            {
                string candidate = Path.Combine(sdkRoot, version);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
    }

    return Directory.GetDirectories(sdkRoot)
        .OrderBy(static path => SdkVersionSortKey(Path.GetFileName(path)), StringComparer.OrdinalIgnoreCase)
        .LastOrDefault();
}

static string? FindGlobalJson(string start)
{
    for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, "global.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}

static string SdkVersionSortKey(string version)
{
    string[] parts = version.Split(['.', '-']);
    return string.Join(".", parts.Select(static part => int.TryParse(part, out int value) ? value.ToString("D8") : part));
}

[MethodImpl(MethodImplOptions.NoInlining)]
static int Run(string[] args) => CommandLine.Run(args);

internal static class CommandLine
{
    public static int Run(string[] args)
    {
        try
        {
            string command = args[0].ToLowerInvariant();
            string[] rest = args.Skip(1).ToArray();

            return command switch
            {
                "dynamic" => RunDynamic(rest),
                "static" => RunStatic(rest),
                "compare-replay" => RunCompareReplay(rest),
                _ => Unknown(command)
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            PrintUsage();
            return 1;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("""
            usage:
              StaticGraphValidation dynamic <binlog> [options]
              StaticGraphValidation static <project> [options]
              StaticGraphValidation compare-replay --dynamic baseline.json --phase1 query.json --static replay.json [options]

            options:
              -p|--property Name=Value   global property for static capture (repeatable)
              -t|--target Target[;...]   entry targets for static target-list capture
              -o|--output path           output path (default: stdout)
              --repo-root path           path used for relative display
              --format text|json         output format for compare-replay (default: text)
            """);
    }

    private static int RunDynamic(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        Options options = Options.Parse(args.Skip(1));
        GraphCapture capture = DynamicBinlogCapture.Capture(args[0], options);
        WriteText(GraphJson.Serialize(capture), options.OutputPath);
        return DiagnosticsExitCode(capture);
    }

    private static int RunStatic(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        Options options = Options.Parse(args.Skip(1));
        GraphCapture capture = StaticGraphCapture.Capture(args[0], options);
        WriteText(GraphJson.Serialize(capture), options.OutputPath);
        return DiagnosticsExitCode(capture);
    }

    private static int RunCompareReplay(string[] args)
    {
        Options options = Options.Parse(args);
        if (options.DynamicPath is null || options.Phase1Path is null || options.StaticPath is null)
        {
            PrintUsage();
            return 1;
        }

        ReplayComparison comparison = ReplayComparer.Compare(
            GraphJson.Read(options.DynamicPath),
            GraphJson.Read(options.Phase1Path),
            GraphJson.Read(options.StaticPath));

        string report = options.Format.Equals("json", StringComparison.OrdinalIgnoreCase)
            ? GraphJson.Serialize(comparison)
            : ReplayReportWriter.Write(comparison);
        WriteText(report, options.OutputPath);
        return comparison.QueryDynamicOnly.Count == 0 &&
            comparison.QueryPhase1Only.Count == 0 &&
            comparison.FullPresenceDynamicOnly.Count == 0 &&
            comparison.FullPresenceReplayOnly.Count == 0 &&
            comparison.BuildIdentityDynamicOnly.Count == 0 &&
            comparison.BuildIdentityStaticOnly.Count == 0 &&
            comparison.QueryEdgeDynamicOnly.Count == 0 &&
            comparison.QueryEdgePhase1Only.Count == 0 &&
            comparison.FullPresenceEdgeDynamicOnly.Count == 0 &&
            comparison.FullPresenceEdgeReplayOnly.Count == 0 &&
            comparison.BuildPresenceEdgeDynamicOnly.Count == 0 &&
            comparison.BuildPresenceEdgeStaticOnly.Count == 0
                ? 0
                : 2;
    }

    private static void WriteText(string text, string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(text);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllText(outputPath, text);
    }

    private static int DiagnosticsExitCode(GraphCapture capture) =>
        capture.Diagnostics.Any(static d => d.Severity.Equals("error", StringComparison.OrdinalIgnoreCase)) ? 2 : 0;

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'");
        PrintUsage();
        return 1;
    }
}

internal sealed class Options
{
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string[] Targets { get; private set; } = [];
    public string? OutputPath { get; private set; }
    public string? RepoRoot { get; private set; }
    public string Format { get; private set; } = "text";
    public string? DynamicPath { get; private set; }
    public string? Phase1Path { get; private set; }
    public string? StaticPath { get; private set; }

    public static Options Parse(IEnumerable<string> args)
    {
        Options options = new();
        string[] array = args.ToArray();
        for (int i = 0; i < array.Length; i++)
        {
            string arg = array[i];
            string Next()
            {
                if (++i >= array.Length)
                {
                    throw new ArgumentException($"Missing value for {arg}");
                }

                return array[i];
            }

            switch (arg)
            {
                case "-p":
                case "--property":
                    AddProperty(options.Properties, Next());
                    break;
                case "-t":
                case "--target":
                    options.Targets = SplitTargets(Next());
                    break;
                case "-o":
                case "--output":
                    options.OutputPath = Next();
                    break;
                case "--repo-root":
                    options.RepoRoot = Path.GetFullPath(Next());
                    break;
                case "--format":
                    options.Format = Next();
                    break;
                case "--dynamic":
                    options.DynamicPath = Next();
                    break;
                case "--phase1":
                    options.Phase1Path = Next();
                    break;
                case "--static":
                    options.StaticPath = Next();
                    break;
                default:
                    if (arg.StartsWith("-p:", StringComparison.OrdinalIgnoreCase))
                    {
                        AddProperty(options.Properties, arg[3..]);
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown option '{arg}'");
                    }
                    break;
            }
        }

        return options;
    }

    private static string[] SplitTargets(string value) =>
        value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void AddProperty(Dictionary<string, string> properties, string value)
    {
        int equals = value.IndexOf('=');
        if (equals <= 0)
        {
            throw new ArgumentException($"Property must be Name=Value: '{value}'");
        }

        properties[value[..equals]] = value[(equals + 1)..];
    }
}

internal sealed record GraphCapture
{
    public string Schema { get; init; } = "static-graph-validation.v1";
    public string Kind { get; init; } = "";
    public string? EntryProject { get; init; }
    public IReadOnlyList<string> EntryTargets { get; init; } = [];
    public IReadOnlyDictionary<string, string> GlobalProperties { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<GraphNode> Nodes { get; init; } = [];
    public IReadOnlyList<GraphEdge> Edges { get; init; } = [];
    public IReadOnlyList<GraphDiagnostic> Diagnostics { get; init; } = [];
}

internal sealed record GraphNode
{
    public string Id { get; init; } = "";
    public string ProjectPath { get; init; } = "";
    public string? RelativePath { get; init; }
    public IReadOnlyList<string> RequestedTargets { get; init; } = [];
    public IReadOnlyList<string> DefaultTargets { get; init; } = [];
    public IReadOnlyDictionary<string, string> GlobalProperties { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> EvaluatedProperties { get; init; } = new Dictionary<string, string>();
    public string IdentityKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
}

internal sealed record GraphEdge
{
    public string From { get; init; } = "";
    public string To { get; init; } = "";
    public IReadOnlyList<string> RequestedTargets { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> GlobalProperties { get; init; } = new Dictionary<string, string>();
    public string SourceKind { get; init; } = "";
    public string? SourceLocation { get; init; }
}

internal sealed record GraphDiagnostic
{
    public string Severity { get; init; } = "info";
    public string Message { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed record ReplayComparison
{
    public int DynamicNodeCount { get; init; }
    public int Phase1NodeCount { get; init; }
    public int StaticNodeCount { get; init; }
    public int DynamicEdgeCount { get; init; }
    public int Phase1EdgeCount { get; init; }
    public int StaticEdgeCount { get; init; }
    public int DynamicQueryCount { get; init; }
    public int Phase1QueryCount { get; init; }
    public int DynamicNonQueryCount { get; init; }
    public int StaticReplayCount { get; init; }
    public int DynamicQueryEdgeCount { get; init; }
    public int Phase1QueryEdgeCount { get; init; }
    public int DynamicNonQueryEdgeCount { get; init; }
    public int StaticReplayEdgeCount { get; init; }
    public IReadOnlyList<NormalizedNode> QueryDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> QueryPhase1Only { get; init; } = [];
    public IReadOnlyList<NormalizedNode> FullPresenceDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> FullPresenceReplayOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> BuildPresenceDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> BuildPresenceStaticOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> BuildIdentityDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedNode> BuildIdentityStaticOnly { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> QueryEdgeDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> QueryEdgePhase1Only { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> FullPresenceEdgeDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> FullPresenceEdgeReplayOnly { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> BuildPresenceEdgeDynamicOnly { get; init; } = [];
    public IReadOnlyList<NormalizedEdge> BuildPresenceEdgeStaticOnly { get; init; } = [];
}

internal enum ComparisonProjection
{
    FullPresence,
    QueryStrict,
    BuildPresence,
    BuildStrict
}

internal enum IdentitySource
{
    None,
    Global
}

internal sealed record IdentityDimension(string Value, IdentitySource Source);

internal sealed record CanonicalNode(
    string ProjectPath,
    string Role,
    IReadOnlyList<string> RequestedTargets,
    IdentityDimension Configuration,
    IdentityDimension TargetFramework,
    IdentityDimension RuntimeIdentifier,
    IdentityDimension TargetOS,
    IdentityDimension TargetArchitecture,
    IdentityDimension TargetRid);

internal sealed record CanonicalEdge(CanonicalNode From, CanonicalNode To, string Role);

internal sealed record NormalizedNode(
    string ProjectPath,
    string Configuration,
    string TargetFramework,
    string RuntimeIdentifier,
    string TargetOS,
    string TargetArchitecture,
    string TargetRid,
    string Role)
{
    public string DisplayName
    {
        get
        {
            List<string> properties = [];
            if (!string.IsNullOrEmpty(TargetFramework))
            {
                properties.Add($"TargetFramework={TargetFramework}");
            }

            if (!string.IsNullOrEmpty(Configuration))
            {
                properties.Add($"Configuration={Configuration}");
            }

            if (!string.IsNullOrEmpty(RuntimeIdentifier))
            {
                properties.Add($"RuntimeIdentifier={RuntimeIdentifier}");
            }

            if (!string.IsNullOrEmpty(TargetOS))
            {
                properties.Add($"TargetOS={TargetOS}");
            }

            if (!string.IsNullOrEmpty(TargetArchitecture))
            {
                properties.Add($"TargetArchitecture={TargetArchitecture}");
            }

            if (!string.IsNullOrEmpty(TargetRid))
            {
                properties.Add($"TargetRid={TargetRid}");
            }

            return properties.Count == 0 ? ProjectPath : $"{ProjectPath} [{string.Join(", ", properties)}]";
        }
    }
}

internal sealed record NormalizedEdge(NormalizedNode From, NormalizedNode To, string Role)
{
    public string DisplayName => $"{From.DisplayName} -> {To.DisplayName} [{Role}]";
}

internal static class GraphJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static GraphCapture Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<GraphCapture>(stream, Options)
            ?? throw new InvalidOperationException($"Unable to read graph capture '{path}'.");
    }
}

internal static class Identity
{
    public static string NodeKey(string projectPath, IReadOnlyDictionary<string, string> globalProperties, IEnumerable<string> requestedTargets)
    {
        string path = Path.GetFullPath(projectPath).Replace('\\', '/');
        string properties = string.Join(";", globalProperties.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key}={pair.Value}"));
        string targets = string.Join(";", requestedTargets);
        return $"{path}|targets={targets}|globals={properties}";
    }
}

internal static class Paths
{
    public static string? FindRepoRoot(string path)
    {
        DirectoryInfo? directory = File.Exists(path) ? new FileInfo(path).Directory : new DirectoryInfo(path);
        for (; directory is not null; directory = directory.Parent)
        {
            string gitPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitPath) || File.Exists(gitPath))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    public static string Relativize(string path, string? repoRoot)
    {
        string fullPath = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            string root = Path.GetFullPath(repoRoot);
            if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetRelativePath(root, fullPath).Replace('\\', '/');
            }
        }

        return fullPath.Replace('\\', '/');
    }
}

internal static class StaticGraphCapture
{
    private static readonly string[] s_identityProperties =
    [
        "TargetFramework",
        "TargetFrameworks",
        "RuntimeIdentifier",
        "RuntimeIdentifiers",
        "Configuration",
        "Platform",
        "TargetOS",
        "TargetArchitecture",
        "BuildArchitecture",
        "TargetRid",
        "IsGraphBuild"
    ];

    public static GraphCapture Capture(string project, Options options)
    {
        string fullProject = Path.GetFullPath(project);
        string repoRoot = options.RepoRoot ?? Paths.FindRepoRoot(fullProject) ?? Directory.GetCurrentDirectory();
        Dictionary<string, string> globals = new(options.Properties, StringComparer.OrdinalIgnoreCase);

        List<GraphDiagnostic> diagnostics = [];
        ProjectGraph graph;
        try
        {
            graph = new ProjectGraph(
                new[] { new ProjectGraphEntryPoint(fullProject, globals) },
                ProjectCollection.GlobalProjectCollection,
                projectInstanceFactory: null);
        }
        catch (Exception ex)
        {
            return new GraphCapture
            {
                Kind = "static",
                EntryProject = fullProject,
                EntryTargets = options.Targets,
                GlobalProperties = globals,
                Diagnostics = [new GraphDiagnostic { Severity = "error", Message = ex.Message, Context = ex.ToString() }]
            };
        }

        Dictionary<ProjectGraphNode, string[]> targetLists = new();
        if (options.Targets.Length > 0)
        {
            try
            {
                foreach ((ProjectGraphNode node, IReadOnlyCollection<string> targets) in graph.GetTargetLists(options.Targets))
                {
                    targetLists[node] = targets.ToArray();
                }
            }
            catch (Exception ex)
            {
                diagnostics.Add(new GraphDiagnostic { Severity = "error", Message = ex.Message, Context = ex.ToString() });
            }
        }

        Dictionary<ProjectGraphNode, GraphNode> nodeMap = new();
        foreach (ProjectGraphNode graphNode in graph.ProjectNodes.OrderBy(static node => node.ProjectInstance.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            string[] requestedTargets = targetLists.TryGetValue(graphNode, out string[]? targets) ? targets : [];
            GraphNode node = CreateNode(graphNode.ProjectInstance, repoRoot, requestedTargets);
            nodeMap[graphNode] = node;
        }

        List<GraphEdge> edges = [];
        foreach (ProjectGraphNode source in graph.ProjectNodes)
        {
            foreach (ProjectGraphNode target in source.ProjectReferences)
            {
                ProjectItemInstance? projectReference = FindProjectReference(source.ProjectInstance, target.ProjectInstance.FullPath);
                edges.Add(new GraphEdge
                {
                    From = nodeMap[source].Id,
                    To = nodeMap[target].Id,
                    RequestedTargets = SplitTargets(projectReference?.GetMetadataValue("Targets")),
                    Metadata = projectReference is null ? new Dictionary<string, string>() : ProjectReferenceMetadata(projectReference),
                    GlobalProperties = CopyProperties(target.ProjectInstance.GlobalProperties),
                    SourceKind = "static-project-reference",
                    SourceLocation = source.ProjectInstance.FullPath
                });
            }
        }

        return new GraphCapture
        {
            Kind = "static",
            EntryProject = fullProject,
            EntryTargets = options.Targets,
            GlobalProperties = globals,
            Nodes = nodeMap.Values.OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(),
            Edges = edges.OrderBy(static edge => edge.From).ThenBy(static edge => edge.To).ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static GraphNode CreateNode(ProjectInstance project, string repoRoot, IReadOnlyList<string> requestedTargets)
    {
        Dictionary<string, string> evaluated = new(StringComparer.OrdinalIgnoreCase);
        foreach (string propertyName in s_identityProperties)
        {
            string value = project.GetPropertyValue(propertyName);
            if (!string.IsNullOrEmpty(value))
            {
                evaluated[propertyName] = value;
            }
        }

        Dictionary<string, string> globalProperties = CopyProperties(project.GlobalProperties);
        string key = Identity.NodeKey(project.FullPath, globalProperties, requestedTargets);
        string relativePath = Paths.Relativize(project.FullPath, repoRoot);
        string suffix = string.Join(", ", evaluated.Where(static pair => pair.Key is "TargetFramework" or "RuntimeIdentifier" or "Configuration")
            .Select(static pair => $"{pair.Key}={pair.Value}"));

        return new GraphNode
        {
            Id = key,
            ProjectPath = project.FullPath,
            RelativePath = relativePath,
            RequestedTargets = requestedTargets,
            DefaultTargets = project.DefaultTargets.ToArray(),
            GlobalProperties = globalProperties,
            EvaluatedProperties = evaluated,
            IdentityKey = key,
            DisplayName = string.IsNullOrEmpty(suffix) ? relativePath : $"{relativePath} [{suffix}]"
        };
    }

    private static ProjectItemInstance? FindProjectReference(ProjectInstance source, string targetPath)
    {
        string normalizedTarget = Path.GetFullPath(targetPath);
        return source.GetItems("ProjectReference")
            .FirstOrDefault(item => string.Equals(Path.GetFullPath(item.GetMetadataValue("FullPath")), normalizedTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, string> CopyProperties(IDictionary<string, string> properties) =>
        properties.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> ProjectReferenceMetadata(ProjectItemInstance item)
    {
        string[] names =
        [
            "Targets",
            "AdditionalProperties",
            "GlobalPropertiesToRemove",
            "SetTargetFramework",
            "SetPlatform",
            "SetConfiguration",
            "BuildReference",
            "ReferenceOutputAssembly",
            "SkipGetTargetFrameworkProperties",
            "OutputItemType",
            "FullPath"
        ];

        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Identity"] = item.EvaluatedInclude
        };
        foreach (string name in names)
        {
            string value = item.GetMetadataValue(name);
            if (!string.IsNullOrEmpty(value))
            {
                metadata[name] = value;
            }
        }

        return metadata;
    }

    private static string[] SplitTargets(string? targets) =>
        string.IsNullOrWhiteSpace(targets)
            ? []
            : targets.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class DynamicBinlogCapture
{
    public static GraphCapture Capture(string binlogPath, Options options)
    {
        string fullPath = Path.GetFullPath(binlogPath);
        string repoRoot = options.RepoRoot ?? Paths.FindRepoRoot(Directory.GetCurrentDirectory()) ?? Directory.GetCurrentDirectory();
        List<GraphDiagnostic> diagnostics = [];

        object? build;
        try
        {
            Type binaryLogType = Type.GetType("Microsoft.Build.Logging.StructuredLogger.BinaryLog, StructuredLogger", throwOnError: true)!;
            MethodInfo readBuild = binaryLogType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => method.Name == "ReadBuild" && method.GetParameters().Length >= 1 && method.GetParameters()[0].ParameterType == typeof(string));
            build = readBuild.Invoke(null, [fullPath]);
        }
        catch (Exception ex)
        {
            return new GraphCapture
            {
                Kind = "dynamic",
                EntryProject = fullPath,
                Diagnostics = [new GraphDiagnostic { Severity = "error", Message = $"Unable to read binlog '{fullPath}': {ex.Message}", Context = ex.ToString() }]
            };
        }

        Dictionary<string, GraphNode> nodes = new(StringComparer.Ordinal);
        List<GraphEdge> edges = [];
        Stack<GraphNode> parentStack = new();
        Stack<string> targetStack = new();
        Stack<string> taskStack = new();

        void Walk(object node)
        {
            string typeName = node.GetType().Name;
            bool isProject = typeName.Equals("Project", StringComparison.OrdinalIgnoreCase);
            bool isTarget = typeName.Equals("Target", StringComparison.OrdinalIgnoreCase);
            bool isTask = typeName.EndsWith("Task", StringComparison.OrdinalIgnoreCase);

            if (isTarget)
            {
                targetStack.Push(GetString(node, "Name") ?? "");
            }
            else if (isTask)
            {
                taskStack.Push(GetString(node, "Name") ?? typeName);
            }

            GraphNode? graphNode = null;
            if (isProject)
            {
                graphNode = CreateDynamicNode(node, repoRoot);
                nodes.TryAdd(graphNode.Id, graphNode);

                if (parentStack.TryPeek(out GraphNode? parent))
                {
                    edges.Add(new GraphEdge
                    {
                        From = parent.Id,
                        To = graphNode.Id,
                        RequestedTargets = graphNode.RequestedTargets,
                        Metadata = CreateEdgeMetadata(targetStack, taskStack),
                        GlobalProperties = graphNode.GlobalProperties,
                        SourceKind = taskStack.TryPeek(out string? taskName) && taskName.Equals("MSBuild", StringComparison.OrdinalIgnoreCase)
                            ? "dynamic-msbuild-task"
                            : "dynamic-binlog-parent",
                        SourceLocation = targetStack.TryPeek(out string? targetName) && !string.IsNullOrEmpty(targetName)
                            ? targetName
                            : null
                    });
                }

                parentStack.Push(graphNode);
            }

            foreach (object child in GetChildren(node))
            {
                Walk(child);
            }

            if (isProject)
            {
                parentStack.Pop();
            }

            if (isTarget)
            {
                targetStack.Pop();
            }
            else if (isTask)
            {
                taskStack.Pop();
            }
        }

        Walk(build!);

        diagnostics.Add(new GraphDiagnostic
        {
            Severity = "warning",
            Message = "Dynamic capture is reconstructed from StructuredLogger tree shape. Parent edges are best-effort; verify suspicious edges against the original binlog."
        });

        return new GraphCapture
        {
            Kind = "dynamic",
            EntryProject = fullPath,
            Nodes = nodes.Values.OrderBy(static node => node.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray(),
            Edges = edges.OrderBy(static edge => edge.From).ThenBy(static edge => edge.To).ToArray(),
            Diagnostics = diagnostics
        };
    }

    private static GraphNode CreateDynamicNode(object project, string repoRoot)
    {
        string projectPath = GetString(project, "ProjectFile") ?? GetString(project, "File") ?? GetString(project, "Name") ?? "<unknown>";
        if (File.Exists(projectPath))
        {
            projectPath = Path.GetFullPath(projectPath);
        }

        Dictionary<string, string> properties = ExtractProperties(project);
        string[] targets = ExtractTargets(project);
        string[] defaultTargets = ExtractDefaultTargets(projectPath, properties);
        string key = Identity.NodeKey(projectPath, properties, targets);
        string relativePath = File.Exists(projectPath) ? Paths.Relativize(projectPath, repoRoot) : projectPath;

        return new GraphNode
        {
            Id = key,
            ProjectPath = projectPath,
            RelativePath = relativePath,
            RequestedTargets = targets,
            DefaultTargets = defaultTargets,
            GlobalProperties = properties,
            EvaluatedProperties = ExtractInterestingProperties(properties),
            IdentityKey = key,
            DisplayName = targets.Length == 0 ? relativePath : $"{relativePath} /t:{string.Join(";", targets)}"
        };
    }

    private static Dictionary<string, string> ExtractProperties(object node)
    {
        Dictionary<string, string> properties = new(StringComparer.OrdinalIgnoreCase);
        foreach (string propertyName in new[] { "GlobalProperties", "Properties" })
        {
            object? value = GetProperty(node, propertyName);
            if (value is null)
            {
                continue;
            }

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (object? key in dictionary.Keys)
                {
                    if (key is not null && dictionary[key] is not null)
                    {
                        properties[key.ToString()!] = dictionary[key]!.ToString()!;
                    }
                }
            }
        }

        return properties;
    }

    private static Dictionary<string, string> ExtractInterestingProperties(Dictionary<string, string> properties)
    {
        string[] names = ["TargetFramework", "RuntimeIdentifier", "Configuration", "TargetOS", "TargetArchitecture"];
        return properties.Where(pair => names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static string[] ExtractTargets(object project)
    {
        if (GetProperty(project, "EntryTargets") is System.Collections.IEnumerable entryTargets)
        {
            string[] targets = entryTargets
                .Cast<object?>()
                .Select(static target => target?.ToString())
                .Where(static target => !string.IsNullOrWhiteSpace(target))
                .Select(static target => target!)
                .ToArray();
            if (targets.Length > 0)
            {
                return targets;
            }
        }

        string? text = GetString(project, "TargetsText")
            ?? GetString(project, "TargetNames")
            ?? GetString(project, "Targets")
            ?? GetString(project, "RequestedTargets");

        return string.IsNullOrWhiteSpace(text)
            ? []
            : text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] ExtractDefaultTargets(string projectPath, Dictionary<string, string> properties)
    {
        if (!File.Exists(projectPath))
        {
            return [];
        }

        try
        {
            ProjectInstance project = new(projectPath, properties, toolsVersion: null);
            return project.DefaultTargets.ToArray();
        }
        catch
        {
            return [];
        }
    }

    private static Dictionary<string, string> CreateEdgeMetadata(Stack<string> targetStack, Stack<string> taskStack)
    {
        Dictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
        if (targetStack.TryPeek(out string? targetName) && !string.IsNullOrEmpty(targetName))
        {
            metadata["ParentTarget"] = targetName;
        }

        if (taskStack.TryPeek(out string? taskName) && !string.IsNullOrEmpty(taskName))
        {
            metadata["ParentTask"] = taskName;
        }

        return metadata;
    }

    private static IEnumerable<object> GetChildren(object node)
    {
        object? children = GetProperty(node, "Children");
        if (children is System.Collections.IEnumerable enumerable)
        {
            foreach (object? child in enumerable)
            {
                if (child is not null)
                {
                    yield return child;
                }
            }
        }
    }

    private static string? GetString(object obj, string propertyName) => GetProperty(obj, propertyName)?.ToString();

    private static object? GetProperty(object obj, string propertyName) =>
        obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(obj);
}

internal static class ReplayComparer
{
    private static readonly StringComparer s_comparer = StringComparer.OrdinalIgnoreCase;
    private static readonly HashSet<string> s_queryTargets = new(s_comparer)
    {
        "GetTargetFrameworks",
        "GetTargetFrameworksWithPlatformForSingleTargetFramework"
    };

    public static ReplayComparison Compare(GraphCapture dynamicCapture, GraphCapture phase1Capture, GraphCapture staticCapture)
    {
        Dictionary<string, GraphNode> dynamicNodesById = NodesById(dynamicCapture);
        Dictionary<string, GraphNode> phase1NodesById = NodesById(phase1Capture);
        Dictionary<string, GraphNode> staticNodesById = NodesById(staticCapture);

        HashSet<NormalizedNode> dynamicAllPresence = ProjectNodes(dynamicCapture.Nodes, ComparisonProjection.FullPresence);
        HashSet<NormalizedNode> dynamicQuery = ProjectNodes(dynamicCapture.Nodes.Where(IsQueryOnlyNode), ComparisonProjection.QueryStrict);
        HashSet<NormalizedNode> dynamicNonQueryPresence = ProjectNodes(dynamicCapture.Nodes.Where(static node => !IsQueryOnlyNode(node)), ComparisonProjection.BuildPresence);
        HashSet<NormalizedNode> dynamicNonQueryIdentity = ProjectNodes(dynamicCapture.Nodes.Where(static node => !IsQueryOnlyNode(node)), ComparisonProjection.BuildStrict);

        HashSet<NormalizedNode> phase1Query = ProjectNodes(phase1Capture.Nodes.Where(IsQueryOnlyNode), ComparisonProjection.QueryStrict);
        HashSet<NormalizedNode> phase1QueryPresence = ProjectNodes(phase1Capture.Nodes.Where(IsQueryOnlyNode), ComparisonProjection.FullPresence);
        HashSet<NormalizedNode> staticReplayPresence = ProjectNodes(staticCapture.Nodes, ComparisonProjection.BuildPresence);
        HashSet<NormalizedNode> staticReplayIdentity = ProjectNodes(staticCapture.Nodes, ComparisonProjection.BuildStrict);
        HashSet<NormalizedNode> replayCombinedPresence = new(phase1QueryPresence, NormalizedNodeComparer.Instance);
        replayCombinedPresence.UnionWith(staticReplayPresence);

        HashSet<NormalizedEdge> dynamicAllPresenceEdges = ProjectEdges(dynamicCapture.Edges, dynamicNodesById, ComparisonProjection.FullPresence, EdgeClass.All);
        HashSet<NormalizedEdge> dynamicQueryEdges = ProjectEdges(dynamicCapture.Edges, dynamicNodesById, ComparisonProjection.QueryStrict, EdgeClass.Query);
        HashSet<NormalizedEdge> dynamicNonQueryEdges = ProjectEdges(dynamicCapture.Edges, dynamicNodesById, ComparisonProjection.BuildPresence, EdgeClass.Build);
        HashSet<NormalizedEdge> phase1QueryEdges = ProjectEdges(phase1Capture.Edges, phase1NodesById, ComparisonProjection.QueryStrict, EdgeClass.Query);
        HashSet<NormalizedEdge> phase1QueryPresenceEdges = ProjectEdges(phase1Capture.Edges, phase1NodesById, ComparisonProjection.FullPresence, EdgeClass.Query);
        HashSet<NormalizedEdge> staticReplayEdges = ProjectEdges(staticCapture.Edges, staticNodesById, ComparisonProjection.BuildPresence, EdgeClass.All);
        HashSet<NormalizedEdge> replayCombinedPresenceEdges = new(phase1QueryPresenceEdges, NormalizedEdgeComparer.Instance);
        replayCombinedPresenceEdges.UnionWith(staticReplayEdges);

        return new ReplayComparison
        {
            DynamicNodeCount = dynamicCapture.Nodes.Count,
            Phase1NodeCount = phase1Capture.Nodes.Count,
            StaticNodeCount = staticCapture.Nodes.Count,
            DynamicEdgeCount = dynamicCapture.Edges.Count,
            Phase1EdgeCount = phase1Capture.Edges.Count,
            StaticEdgeCount = staticCapture.Edges.Count,
            DynamicQueryCount = dynamicQuery.Count,
            Phase1QueryCount = phase1Query.Count,
            DynamicNonQueryCount = dynamicNonQueryPresence.Count,
            StaticReplayCount = staticReplayPresence.Count,
            DynamicQueryEdgeCount = dynamicQueryEdges.Count,
            Phase1QueryEdgeCount = phase1QueryEdges.Count,
            DynamicNonQueryEdgeCount = dynamicNonQueryEdges.Count,
            StaticReplayEdgeCount = staticReplayEdges.Count,
            QueryDynamicOnly = Except(dynamicQuery, phase1Query),
            QueryPhase1Only = Except(phase1Query, dynamicQuery),
            FullPresenceDynamicOnly = Except(dynamicAllPresence, replayCombinedPresence),
            FullPresenceReplayOnly = Except(replayCombinedPresence, dynamicAllPresence),
            BuildPresenceDynamicOnly = Except(dynamicNonQueryPresence, staticReplayPresence),
            BuildPresenceStaticOnly = Except(staticReplayPresence, dynamicNonQueryPresence),
            BuildIdentityDynamicOnly = Except(dynamicNonQueryIdentity, staticReplayIdentity),
            BuildIdentityStaticOnly = Except(staticReplayIdentity, dynamicNonQueryIdentity),
            QueryEdgeDynamicOnly = Except(dynamicQueryEdges, phase1QueryEdges),
            QueryEdgePhase1Only = Except(phase1QueryEdges, dynamicQueryEdges),
            FullPresenceEdgeDynamicOnly = Except(dynamicAllPresenceEdges, replayCombinedPresenceEdges),
            FullPresenceEdgeReplayOnly = Except(replayCombinedPresenceEdges, dynamicAllPresenceEdges),
            BuildPresenceEdgeDynamicOnly = Except(dynamicNonQueryEdges, staticReplayEdges),
            BuildPresenceEdgeStaticOnly = Except(staticReplayEdges, dynamicNonQueryEdges)
        };
    }

    private static Dictionary<string, GraphNode> NodesById(GraphCapture capture) =>
        capture.Nodes.ToDictionary(static node => node.Id, static node => node, StringComparer.Ordinal);

    private static HashSet<NormalizedNode> ProjectNodes(IEnumerable<GraphNode> nodes, ComparisonProjection projection)
    {
        HashSet<NormalizedNode> normalizedNodes = new(NormalizedNodeComparer.Instance);
        foreach (GraphNode node in nodes)
        {
            normalizedNodes.Add(Project(ToCanonicalNode(node), projection));
        }

        return normalizedNodes;
    }

    private static CanonicalNode ToCanonicalNode(GraphNode node)
    {
        string projectPath = node.RelativePath ?? node.ProjectPath.Replace('\\', '/');
        string role = IsQueryOnlyNode(node) ? $"Query:{string.Join(";", node.RequestedTargets)}" : "Build";
        return new CanonicalNode(
            projectPath,
            role,
            node.RequestedTargets,
            GetDimension(node, "Configuration"),
            GetDimension(node, "TargetFramework"),
            GetDimension(node, "RuntimeIdentifier"),
            GetDimension(node, "TargetOS"),
            GetDimension(node, "TargetArchitecture"),
            GetDimension(node, "TargetRid"));
    }

    private static NormalizedNode Project(CanonicalNode node, ComparisonProjection projection)
    {
        string targetFramework = projection is ComparisonProjection.QueryStrict or ComparisonProjection.BuildStrict ? node.TargetFramework.Value : "";
        string runtimeIdentifier = projection == ComparisonProjection.BuildStrict && node.RuntimeIdentifier.Source == IdentitySource.Global ? node.RuntimeIdentifier.Value : "";
        string targetOS = projection == ComparisonProjection.BuildStrict && node.TargetOS.Source == IdentitySource.Global ? node.TargetOS.Value : "";
        string targetArchitecture = projection == ComparisonProjection.BuildStrict && node.TargetArchitecture.Source == IdentitySource.Global ? node.TargetArchitecture.Value : "";
        string targetRid = projection == ComparisonProjection.BuildStrict && node.TargetRid.Source == IdentitySource.Global ? node.TargetRid.Value : "";
        string role = projection == ComparisonProjection.FullPresence ? node.Role.Split(':')[0] : node.Role;
        return new NormalizedNode(node.ProjectPath, node.Configuration.Value, targetFramework, runtimeIdentifier, targetOS, targetArchitecture, targetRid, role);
    }

    private static HashSet<NormalizedEdge> ProjectEdges(
        IEnumerable<GraphEdge> edges,
        Dictionary<string, GraphNode> nodesById,
        ComparisonProjection projection,
        EdgeClass edgeClass)
    {
        HashSet<NormalizedEdge> normalizedEdges = new(NormalizedEdgeComparer.Instance);
        foreach (GraphEdge edge in edges)
        {
            if (!nodesById.TryGetValue(edge.From, out GraphNode? from) ||
                !nodesById.TryGetValue(edge.To, out GraphNode? to))
            {
                continue;
            }

            bool isQueryEdge = IsQueryOnlyNode(to);
            if (edgeClass == EdgeClass.Query && !isQueryEdge)
            {
                continue;
            }

            if (edgeClass == EdgeClass.Build && isQueryEdge)
            {
                continue;
            }

            NormalizedNode normalizedFrom = Project(ToCanonicalNode(from), projection);
            NormalizedNode normalizedTo = Project(ToCanonicalNode(to), projection);
            string role = isQueryEdge ? "Query" : "Build";
            normalizedEdges.Add(new NormalizedEdge(normalizedFrom, normalizedTo, role));
        }

        return normalizedEdges;
    }

    private static IdentityDimension GetDimension(GraphNode node, string name)
    {
        if (node.GlobalProperties.TryGetValue(name, out string? globalValue))
        {
            return new IdentityDimension(globalValue, IdentitySource.Global);
        }

        return new IdentityDimension("", IdentitySource.None);
    }

    private static bool IsQueryOnlyNode(GraphNode node)
    {
        if (node.RequestedTargets.Count == 0)
        {
            return false;
        }

        return node.RequestedTargets.All(s_queryTargets.Contains);
    }

    private static List<NormalizedNode> Except(HashSet<NormalizedNode> left, HashSet<NormalizedNode> right) =>
        left.Except(right, NormalizedNodeComparer.Instance)
            .OrderBy(static node => node.ProjectPath, s_comparer)
            .ThenBy(static node => node.Configuration, s_comparer)
            .ThenBy(static node => node.TargetFramework, s_comparer)
            .ThenBy(static node => node.Role, s_comparer)
            .ToList();

    private static List<NormalizedEdge> Except(HashSet<NormalizedEdge> left, HashSet<NormalizedEdge> right) =>
        left.Except(right, NormalizedEdgeComparer.Instance)
            .OrderBy(static edge => edge.From.ProjectPath, s_comparer)
            .ThenBy(static edge => edge.To.ProjectPath, s_comparer)
            .ThenBy(static edge => edge.Role, s_comparer)
            .ThenBy(static edge => edge.From.Configuration, s_comparer)
            .ThenBy(static edge => edge.To.Configuration, s_comparer)
            .ThenBy(static edge => edge.From.TargetFramework, s_comparer)
            .ThenBy(static edge => edge.To.TargetFramework, s_comparer)
            .ToList();
}

internal enum EdgeClass
{
    All,
    Query,
    Build
}

internal sealed class NormalizedNodeComparer : IEqualityComparer<NormalizedNode>
{
    public static NormalizedNodeComparer Instance { get; } = new();

    public bool Equals(NormalizedNode? x, NormalizedNode? y) =>
        x is not null &&
        y is not null &&
        StringComparer.OrdinalIgnoreCase.Equals(x.ProjectPath, y.ProjectPath) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.Configuration, y.Configuration) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.TargetFramework, y.TargetFramework) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.RuntimeIdentifier, y.RuntimeIdentifier) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.TargetOS, y.TargetOS) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.TargetArchitecture, y.TargetArchitecture) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.TargetRid, y.TargetRid) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.Role, y.Role);

    public int GetHashCode(NormalizedNode obj) =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProjectPath),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Configuration),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetFramework),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RuntimeIdentifier),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetOS),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetArchitecture),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetRid),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Role));
}

internal sealed class NormalizedEdgeComparer : IEqualityComparer<NormalizedEdge>
{
    public static NormalizedEdgeComparer Instance { get; } = new();

    public bool Equals(NormalizedEdge? x, NormalizedEdge? y) =>
        x is not null &&
        y is not null &&
        NormalizedNodeComparer.Instance.Equals(x.From, y.From) &&
        NormalizedNodeComparer.Instance.Equals(x.To, y.To) &&
        StringComparer.OrdinalIgnoreCase.Equals(x.Role, y.Role);

    public int GetHashCode(NormalizedEdge obj) =>
        HashCode.Combine(
            NormalizedNodeComparer.Instance.GetHashCode(obj.From),
            NormalizedNodeComparer.Instance.GetHashCode(obj.To),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Role));
}

internal static class ReplayReportWriter
{
    public static string Write(ReplayComparison comparison)
    {
        StringWriter writer = new();
        writer.WriteLine("Static graph replay comparison");
        writer.WriteLine();
        writer.WriteLine("Raw node counts:");
        writer.WriteLine($"  dynamic: {comparison.DynamicNodeCount}");
        writer.WriteLine($"  phase1: {comparison.Phase1NodeCount}");
        writer.WriteLine($"  static replay: {comparison.StaticNodeCount}");
        writer.WriteLine("Raw edge counts:");
        writer.WriteLine($"  dynamic: {comparison.DynamicEdgeCount}");
        writer.WriteLine($"  phase1: {comparison.Phase1EdgeCount}");
        writer.WriteLine($"  static replay: {comparison.StaticEdgeCount}");
        writer.WriteLine();
        writer.WriteLine("Normalized node counts:");
        writer.WriteLine($"  dynamic query: {comparison.DynamicQueryCount}");
        writer.WriteLine($"  phase1 query: {comparison.Phase1QueryCount}");
        writer.WriteLine($"  dynamic non-query: {comparison.DynamicNonQueryCount}");
        writer.WriteLine($"  static replay: {comparison.StaticReplayCount}");
        writer.WriteLine("Normalized edge counts:");
        writer.WriteLine($"  dynamic query: {comparison.DynamicQueryEdgeCount}");
        writer.WriteLine($"  phase1 query: {comparison.Phase1QueryEdgeCount}");
        writer.WriteLine($"  dynamic non-query: {comparison.DynamicNonQueryEdgeCount}");
        writer.WriteLine($"  static replay: {comparison.StaticReplayEdgeCount}");
        writer.WriteLine();
        writer.WriteLine("Projection dimensions:");
        writer.WriteLine("  QueryStrict: project + configuration + target framework + query target");
        writer.WriteLine("  FullPresence: project + configuration + role");
        writer.WriteLine("  BuildPresence: project + configuration + build role");
        writer.WriteLine("  BuildStrict: project + configuration + target framework + explicit runtime identity globals");
        writer.WriteLine("  Identity dimensions use global properties only; evaluated properties are diagnostic data and do not participate.");
        writer.WriteLine();
        WriteSection(writer, "QueryStrict dynamic-only", comparison.QueryDynamicOnly);
        WriteSection(writer, "QueryStrict phase1-only", comparison.QueryPhase1Only);
        WriteSection(writer, "FullPresence dynamic-only", comparison.FullPresenceDynamicOnly);
        WriteSection(writer, "FullPresence replay-only", comparison.FullPresenceReplayOnly);
        WriteSection(writer, "BuildPresence dynamic-only", comparison.BuildPresenceDynamicOnly);
        WriteSection(writer, "BuildPresence static-only", comparison.BuildPresenceStaticOnly);
        WriteSection(writer, "BuildStrict dynamic-only", comparison.BuildIdentityDynamicOnly);
        WriteSection(writer, "BuildStrict static-only", comparison.BuildIdentityStaticOnly);
        WriteSection(writer, "QueryStrict edge dynamic-only", comparison.QueryEdgeDynamicOnly);
        WriteSection(writer, "QueryStrict edge phase1-only", comparison.QueryEdgePhase1Only);
        WriteSection(writer, "FullPresence edge dynamic-only", comparison.FullPresenceEdgeDynamicOnly);
        WriteSection(writer, "FullPresence edge replay-only", comparison.FullPresenceEdgeReplayOnly);
        WriteSection(writer, "BuildPresence edge dynamic-only", comparison.BuildPresenceEdgeDynamicOnly);
        WriteSection(writer, "BuildPresence edge static-only", comparison.BuildPresenceEdgeStaticOnly);
        return writer.ToString();
    }

    private static void WriteSection(StringWriter writer, string title, IReadOnlyList<NormalizedNode> nodes)
    {
        writer.WriteLine($"{title}: {nodes.Count}");
        foreach (NormalizedNode node in nodes.Take(80))
        {
            writer.WriteLine($"  {node.DisplayName}");
        }

        if (nodes.Count > 80)
        {
            writer.WriteLine("  ...");
        }

        writer.WriteLine();
    }

    private static void WriteSection(StringWriter writer, string title, IReadOnlyList<NormalizedEdge> edges)
    {
        writer.WriteLine($"{title}: {edges.Count}");
        foreach (NormalizedEdge edge in edges.Take(80))
        {
            writer.WriteLine($"  {edge.DisplayName}");
        }

        if (edges.Count > 80)
        {
            writer.WriteLine("  ...");
        }

        writer.WriteLine();
    }
}
