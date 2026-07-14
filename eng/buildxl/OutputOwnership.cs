// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Execution;

public sealed class OutputOwnership
{
    private readonly RepoPaths _paths;
    private readonly IProjectGraphPolicy _policy;

    public OutputOwnership(RepoPaths paths, IProjectGraphPolicy policy)
    {
        _paths = paths;
        _policy = policy;
    }

    public string[] ComputeOutputDirectories(ProjectInstance project, string relativePath) =>
        CollapseDirectories(ComputeOutputDirectoriesCore(project, relativePath)
            .Concat(_policy.GetAdditionalOutputDirectories(project, relativePath))
            .Where(d => !_policy.ShouldSkipOutputDirectory(relativePath, d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
            .ToArray();

    public string[] ComputeInputFiles(ProjectInstance project, string relativePath) =>
        _policy.GetAdditionalInputFiles(project, relativePath)
            .Concat(GetEvaluatedInputFiles(project, relativePath))
            .Concat(GetProjectReferenceRestoreGeneratedFiles(project))
            .Concat(GetMSBuildAllProjects(project))
            .Concat(GetAncestorMsBuildFiles(project))
            .Concat(GetRestoreGeneratedFiles(project))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public string[] ComputeInputDirectories(ProjectInstance project, string relativePath) =>
        ComputeInputDirectoriesCore(project, relativePath)
            .Concat(_policy.GetAdditionalInputDirectories(project, relativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public NodeModel[] RemoveCrossNodeParentOutputs(NodeModel[] nodes)
    {
        var allOutputs = nodes
            .SelectMany(node => node.OutputDirectories.Select(directory => (Node: node.Id, Directory: RepoPaths.Normalize(directory))))
            .ToArray();

        return nodes
            .Select(node =>
            {
                string[] outputDirectories = node.OutputDirectories
                    .Where(directory =>
                    {
                        string parent = RepoPaths.Normalize(directory);
                        return !allOutputs.Any(other =>
                            !string.Equals(other.Node, node.Id, StringComparison.OrdinalIgnoreCase) &&
                            other.Directory.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
                    })
                    .ToArray();

                return node with { OutputDirectories = outputDirectories };
            })
            .ToArray();
    }

    public NodeModel[] RemoveDuplicateOutputOwners(NodeModel[] nodes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<NodeModel>();
        foreach (NodeModel node in nodes)
        {
            string key = string.Join("|", node.OutputDirectories.Select(RepoPaths.Normalize).Order(StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(key) || seen.Add(key))
            {
                result.Add(node);
            }
        }

        var kept = result.Select(n => n.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return result
            .Select(n => n with { Dependencies = n.Dependencies.Where(kept.Contains).ToArray() })
            .ToArray();
    }

    public OutputCollision[] FindCollisions(NodeModel[] nodes) =>
        nodes
            .SelectMany(n => n.OutputDirectories.Select(d => new { Directory = d, Node = n.Id }))
            .GroupBy(x => RepoPaths.Normalize(x.Directory), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Node).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            .Select(g => new OutputCollision(g.Key, g.Select(x => x.Node).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray()))
            .ToArray();

    private IEnumerable<string> ComputeOutputDirectoriesCore(ProjectInstance project, string relativePath)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        foreach (string propertyName in new[] { "IntermediateOutputPath", "OutputPath", "TargetDir", "PublishDir" })
        {
            string value = project.GetPropertyValue(propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return RepoPaths.FullPath(value, projectDirectory);
            }
        }
    }

    private IEnumerable<string> ComputeInputDirectoriesCore(ProjectInstance project, string relativePath)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        if (!_policy.ShouldSkipProjectDirectoryInput(projectDirectory))
        {
            yield return projectDirectory;
        }
    }

    private IEnumerable<string> GetEvaluatedInputFiles(ProjectInstance project, string relativePath)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        string[] outputDirectories = ComputeOutputDirectories(project, relativePath)
            .Select(RepoPaths.Normalize)
            .ToArray();

        foreach (string itemType in new[]
        {
            "Compile",
            "EmbeddedResource",
            "AdditionalFiles",
            "Analyzer",
            "Content",
            "None",
            "Resource",
            "EditorConfigFiles",
            "ProjectReference",
        })
        {
            foreach (ProjectItemInstance item in project.GetItems(itemType))
            {
                string value = item.GetMetadataValue("FullPath");
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = item.EvaluatedInclude;
                }

                if (value.Contains('`'))
                {
                    continue;
                }

                string fullPath = RepoPaths.FullPath(value, projectDirectory);
                if (IsUnderDirectory(fullPath, _paths.RepoRoot) && File.Exists(fullPath) && !IsUnderAnyDirectory(fullPath, outputDirectories))
                {
                    yield return fullPath;
                }
            }
        }
    }

    private static bool IsUnderAnyDirectory(string path, string[] directories)
    {
        string normalizedPath = RepoPaths.Normalize(path);
        return directories.Any(directory =>
            normalizedPath.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string normalizedPath = RepoPaths.Normalize(path);
        string normalizedDirectory = RepoPaths.Normalize(directory);
        return normalizedPath.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(normalizedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<string> GetAncestorMsBuildFiles(ProjectInstance project)
    {
        string? directory = Path.GetDirectoryName(project.FullPath);
        while (!string.IsNullOrEmpty(directory) &&
            (directory.Equals(_paths.RepoRoot, StringComparison.OrdinalIgnoreCase) ||
             directory.StartsWith(_paths.RepoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string fileName in new[]
            {
                "Directory.Build.props",
                "Directory.Build.targets",
                "Directory.Build.rsp",
                "Directory.Packages.props",
                "NuGet.config",
                "global.json",
                ".editorconfig",
            })
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }

            if (directory.Equals(_paths.RepoRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    private static IEnumerable<string> GetMSBuildAllProjects(ProjectInstance project)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        string value = project.GetPropertyValue("MSBuildAllProjects");
        foreach (string path in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string fullPath = RepoPaths.FullPath(path, projectDirectory);
            if (File.Exists(fullPath))
            {
                yield return fullPath;
            }
        }
    }

    private static IEnumerable<string> GetRestoreGeneratedFiles(ProjectInstance project)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        foreach (string propertyName in new[] { "ProjectAssetsFile" })
        {
            string value = project.GetPropertyValue(propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                string fullPath = RepoPaths.FullPath(value, projectDirectory);
                if (File.Exists(fullPath))
                {
                    yield return fullPath;
                }
            }
        }

        string extensionsPath = project.GetPropertyValue("MSBuildProjectExtensionsPath");
        if (!string.IsNullOrWhiteSpace(extensionsPath))
        {
            string fullExtensionsPath = RepoPaths.FullPath(extensionsPath, projectDirectory);
            string projectFileName = Path.GetFileName(project.FullPath);
            foreach (string fileName in new[]
            {
                $"{projectFileName}.nuget.g.props",
                $"{projectFileName}.nuget.g.targets",
                $"{projectFileName}.nuget.dgspec.json",
            })
            {
                string fullPath = Path.Combine(fullExtensionsPath, fileName);
                if (File.Exists(fullPath))
                {
                    yield return fullPath;
                }
            }
        }
    }

    private IEnumerable<string> GetProjectReferenceRestoreGeneratedFiles(ProjectInstance project)
    {
        string projectDirectory = Path.GetDirectoryName(project.FullPath)!;
        foreach (ProjectItemInstance projectReference in project.GetItems("ProjectReference"))
        {
            string referencePath = projectReference.GetMetadataValue("FullPath");
            if (string.IsNullOrWhiteSpace(referencePath))
            {
                referencePath = RepoPaths.FullPath(projectReference.EvaluatedInclude, projectDirectory);
            }

            string referenceProjectName = Path.GetFileName(referencePath);
            string artifactsObj = Path.Combine(_paths.RepoRoot, "artifacts", "obj");
            foreach (string candidate in Directory.EnumerateFiles(artifactsObj, $"{referenceProjectName}.nuget.g.props", SearchOption.AllDirectories))
            {
                yield return RepoPaths.Normalize(candidate);
            }

            foreach (string candidate in Directory.EnumerateFiles(artifactsObj, $"{referenceProjectName}.nuget.g.targets", SearchOption.AllDirectories))
            {
                yield return RepoPaths.Normalize(candidate);
            }

            foreach (string candidate in Directory.EnumerateFiles(artifactsObj, "project.assets.json", SearchOption.AllDirectories)
                .Where(candidate => Path.GetDirectoryName(candidate)?.Contains(Path.GetFileNameWithoutExtension(referenceProjectName), StringComparison.OrdinalIgnoreCase) == true))
            {
                yield return RepoPaths.Normalize(candidate);
            }

            foreach (string candidate in GetAncestorMsBuildFiles(referencePath))
            {
                yield return candidate;
            }
        }
    }

    private IEnumerable<string> GetAncestorMsBuildFiles(string projectPath)
    {
        string? directory = Path.GetDirectoryName(projectPath);
        while (!string.IsNullOrEmpty(directory) &&
            (directory.Equals(_paths.RepoRoot, StringComparison.OrdinalIgnoreCase) ||
             directory.StartsWith(_paths.RepoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
        {
            foreach (string fileName in new[]
            {
                "Directory.Build.props",
                "Directory.Build.targets",
                "Directory.Build.rsp",
                "Directory.Packages.props",
                "NuGet.config",
                "global.json",
                ".editorconfig",
            })
            {
                string candidate = Path.Combine(directory, fileName);
                if (File.Exists(candidate))
                {
                    yield return candidate;
                }
            }

            if (directory.Equals(_paths.RepoRoot, StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    private static IEnumerable<string> CollapseDirectories(IEnumerable<string> directories)
    {
        var normalized = directories.Select(RepoPaths.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d.Length).ToList();
        for (int i = 0; i < normalized.Count; i++)
        {
            string candidate = normalized[i];
            bool hasParent = normalized.Take(i).Any(parent =>
                candidate.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!hasParent)
            {
                yield return candidate;
            }
        }
    }
}
