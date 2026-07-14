// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Execution;

public sealed class RuntimeProjectGraphPolicy : IProjectGraphPolicy
{
    private readonly GeneratorOptions _options;

    public RuntimeProjectGraphPolicy(GeneratorOptions options)
    {
        _options = options;
    }

    public Dictionary<string, string> CreateGlobalProperties() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["Configuration"] = _options.Configuration,
        ["Restore"] = "false",
        ["Build"] = "true",
        ["Rebuild"] = "false",
        ["Test"] = "false",
        ["Pack"] = "false",
        ["IntegrationTest"] = "false",
        ["PerformanceTest"] = "false",
        ["Sign"] = "false",
        ["Publish"] = "false",
        ["Ninja"] = "true",
        ["Subset"] = _options.Subset,
    };

    public IEnumerable<string> GetAdditionalInputFiles(ProjectInstance project, string relativePath) =>
        GetItems(project, "StaticGraphInputFile");

    public IEnumerable<string> GetAdditionalInputDirectories(ProjectInstance project, string relativePath) =>
        GetItems(project, "StaticGraphInputDirectory");

    public IEnumerable<string> GetAdditionalOutputDirectories(ProjectInstance project, string relativePath) =>
        GetItems(project, "StaticGraphOutputDirectory");

    public bool ShouldSkipOutputDirectory(string relativePath, string directory)
    {
        string normalized = RepoPaths.Normalize(directory);
        string packagesRoot = RepoPaths.Normalize(Path.Combine(_options.RepoRoot, "artifacts", "packages"));
        string coreClrRuntimeBin = RepoPaths.Normalize(Path.Combine(_options.RepoRoot, "artifacts", "bin", "coreclr"));
        if (normalized.Equals(packagesRoot, StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith(packagesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (Path.GetDirectoryName(normalized)?.Equals(coreClrRuntimeBin, StringComparison.OrdinalIgnoreCase) == true &&
            Path.GetFileName(normalized).EndsWith($".{_options.Configuration}", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalized.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}coreclr{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
            (normalized.EndsWith($"{Path.DirectorySeparatorChar}publish", StringComparison.OrdinalIgnoreCase) ||
             normalized.EndsWith($"{Path.DirectorySeparatorChar}aotsdk", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        foreach (string sharedIntermediate in new[]
        {
            Path.Combine(_options.RepoRoot, "artifacts", "obj", "coreclr", "Microsoft.NETCore.ILAsm"),
            Path.Combine(_options.RepoRoot, "artifacts", "obj", "coreclr", "Microsoft.NETCore.ILDAsm"),
            Path.Combine(_options.RepoRoot, "artifacts", "obj", "coreclr", "Microsoft.NETCore.TestHost"),
        })
        {
            string sharedRoot = RepoPaths.Normalize(sharedIntermediate);
            if (normalized.Equals(sharedRoot, StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith(sharedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public bool ShouldSkipProjectDirectoryInput(string projectDirectory)
    {
        string fullPath = RepoPaths.Normalize(projectDirectory);
        string repoRoot = RepoPaths.Normalize(_options.RepoRoot);
        string artifacts = RepoPaths.Normalize(Path.Combine(_options.RepoRoot, "artifacts"));
        string coreclrRoot = RepoPaths.Normalize(Path.Combine(_options.RepoRoot, "src", "coreclr"));
        string coreclrNuget = RepoPaths.Normalize(Path.Combine(_options.RepoRoot, "src", "coreclr", ".nuget"));

        return string.Equals(fullPath, repoRoot, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(artifacts + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, coreclrRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fullPath, coreclrNuget, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(coreclrNuget + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetItems(ProjectInstance project, string itemType) =>
        project.GetItems(itemType)
            .Select(item => item.EvaluatedInclude)
            .Where(value => !string.IsNullOrWhiteSpace(value));
}
