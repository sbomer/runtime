// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

public sealed class MsBuildGraphLoader
{
    private readonly GeneratorOptions _options;
    private readonly RepoPaths _paths;
    private readonly IProjectGraphPolicy _policy;
    private readonly OutputOwnership _ownership;
    private Dictionary<ProjectGraphNode, string> _nodeIds = new();

    public MsBuildGraphLoader(GeneratorOptions options, RepoPaths paths, IProjectGraphPolicy policy, OutputOwnership ownership)
    {
        _options = options;
        _paths = paths;
        _policy = policy;
        _ownership = ownership;
    }

    public NodeModel[] LoadNodes()
    {
        var graph = new ProjectGraph(Path.Combine(_options.RepoRoot, "Build.proj"), _policy.CreateGlobalProperties());
        List<ProjectGraphNode> projectNodes = graph.ProjectNodes.ToList();
        _nodeIds = projectNodes.ToDictionary(n => n, CreateId);

        return TopologicalOrder(projectNodes)
            .Select(CreateNode)
            .ToArray();
    }

    private List<ProjectGraphNode> TopologicalOrder(List<ProjectGraphNode> graphNodes)
    {
        var result = new List<ProjectGraphNode>();
        var visited = new HashSet<ProjectGraphNode>();
        var visiting = new HashSet<ProjectGraphNode>();

        foreach (ProjectGraphNode node in graphNodes.OrderBy(n => _paths.Relative(n.ProjectInstance.FullPath), StringComparer.OrdinalIgnoreCase))
        {
            Visit(node);
        }

        return result;

        void Visit(ProjectGraphNode node)
        {
            if (visited.Contains(node))
            {
                return;
            }

            if (!visiting.Add(node))
            {
                throw new InvalidOperationException($"Cycle detected at {_nodeIds[node]}");
            }

            foreach (ProjectGraphNode reference in node.ProjectReferences.OrderBy(r => _paths.Relative(r.ProjectInstance.FullPath), StringComparer.OrdinalIgnoreCase))
            {
                Visit(reference);
            }

            visiting.Remove(node);
            visited.Add(node);
            result.Add(node);
        }
    }

    private NodeModel CreateNode(ProjectGraphNode node)
    {
        ProjectInstance project = node.ProjectInstance;
        string id = _nodeIds[node];
        string projectPath = project.FullPath;
        string relativePath = _paths.Relative(projectPath);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string[] outputDirectories = _ownership.ComputeOutputDirectories(project, relativePath);
        string[] inputFiles = _ownership.ComputeInputFiles(project, relativePath);
        string[] inputDirectories = _ownership.ComputeInputDirectories(project, relativePath);
        string[] dependencies = node.ProjectReferences.Select(r => _nodeIds[r]).OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
        var properties = project.GlobalProperties
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        string[] targets = project.DefaultTargets.Contains("Build", StringComparer.OrdinalIgnoreCase)
            ? new[] { "Build" }
            : project.DefaultTargets.ToArray();

        return new NodeModel(
            id,
            projectPath,
            relativePath,
            projectDirectory,
            targets,
            properties,
            dependencies,
            outputDirectories,
            inputFiles,
            inputDirectories,
            Path.Combine(_options.OutputDirectory, "cache", id, "output.cache"),
            Path.Combine(_options.OutputDirectory, "logs", id));
    }

    private string CreateId(ProjectGraphNode node)
    {
        string fullName = _paths.Relative(node.ProjectInstance.FullPath).Replace('\\', '/');
        string sanitized = Sanitize(fullName);
        return $"{sanitized}_{HashProperties(node.ProjectInstance.GlobalProperties)[..12]}";
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString().Trim('_');
    }

    private static string HashProperties(IEnumerable<KeyValuePair<string, string>> properties)
    {
        string text = string.Join('\n', properties.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}={kv.Value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
