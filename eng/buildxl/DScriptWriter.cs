// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

public sealed class DScriptWriter
{
    private readonly RepoPaths _paths;
    private readonly GeneratorOptions _options;

    public DScriptWriter(RepoPaths paths, GeneratorOptions options)
    {
        _paths = paths;
        _options = options;
    }

    public string Generate(GraphModel model)
    {
        NodeModel[] scheduledNodes = model.Nodes
            .Where(node => node.OutputDirectories.Length != 0 && !IsTraversalProject(node) && !IsOuterCrossTargetingProject(node))
            .ToArray();
        var scheduledById = scheduledNodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        string[] toolDependencyIds = scheduledNodes
            .Where(node => node.RelativePath.Equals("src/tools/illink/src/ILLink.Tasks/ILLink.Tasks.csproj", StringComparison.OrdinalIgnoreCase) &&
                node.OutputDirectories.Any(outputDirectory => outputDirectory.EndsWith($"{Path.DirectorySeparatorChar}net", StringComparison.OrdinalIgnoreCase)))
            .Select(node => node.Id)
            .ToArray();
        var effectiveDependencies = scheduledNodes.ToDictionary(
            node => node.Id,
            node => GetEffectiveDependencies(node, toolDependencyIds),
            StringComparer.OrdinalIgnoreCase);
        scheduledNodes = TopologicalSort(scheduledNodes, scheduledById, effectiveDependencies);
        string[] allOutputDirectories = scheduledNodes
            .SelectMany(node => node.OutputDirectories)
            .Select(RepoPaths.Normalize)
            .ToArray();
        var sharedOutputDirectories = allOutputDirectories
            .GroupBy(directory => directory, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();
        builder.AppendLine("import {Artifact, Cmd, Transformer} from \"Sdk.Transformers\";");
        builder.AppendLine();
        builder.AppendLine("const repoRoot = d`${Context.getMount(\"RuntimeRepo\").path}`;");
        builder.AppendLine("const dotnetWrapper: Transformer.ToolDefinition = {");
        builder.AppendLine($"    exe: {_paths.FileLiteral(Path.Combine(_options.OutputDirectory, "dotnet-msbuild-wrapper.sh"))},");
        builder.AppendLine("    dependsOnCurrentHostOSDirectories: true,");
        builder.AppendLine("};");
        builder.AppendLine("const dotnetRuntime = Transformer.sealSourceDirectory({ root: d`${repoRoot}/.dotnet`, include: \"allDirectories\" });");
        builder.AppendLine($"const msbuildDll = {_paths.FileLiteral(Path.Combine(_options.SdkPath, "MSBuild.dll"))};");
        builder.AppendLine("const repoEngineering = Transformer.sealSourceDirectory({ root: d`${repoRoot}/eng`, include: \"allDirectories\" });");
        builder.AppendLine("const repoSourceGenerators = [");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/libraries/System.Runtime.InteropServices/gen`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/libraries/System.Private.CoreLib/gen`, include: \"allDirectories\" }),");
        builder.AppendLine("];");
        builder.AppendLine("const repoSharedBuildInputs = [");
        builder.AppendLine("    f`${repoRoot}/src/libraries/NetCoreAppLibrary.props`,");
        builder.AppendLine("    f`${repoRoot}/src/libraries/Microsoft.NETCore.Platforms/src/PortableRuntimeIdentifierGraph.json`,");
        builder.AppendLine("    f`${repoRoot}/src/libraries/Microsoft.NETCore.Platforms/src/runtime.json`,");
        builder.AppendLine("    f`${repoRoot}/src/libraries/System.Private.CoreLib/src/System.Private.CoreLib.Shared.projitems`,");
        builder.AppendLine("    f`${repoRoot}/src/libraries/System.Private.CoreLib/src/CompatibilitySuppressions.xml`,");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/libraries/System.Private.CoreLib/src/ILLink`, include: \"allDirectories\" }),");
        builder.AppendLine("    f`${repoRoot}/src/tools/Directory.Build.props`,");
        builder.AppendLine("    f`${repoRoot}/src/tools/Directory.Build.targets`,");
        builder.AppendLine("    f`${repoRoot}/src/tools/illink/Directory.Build.props`,");
        builder.AppendLine("    f`${repoRoot}/src/tools/illink/src/Directory.Build.props`,");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/ILLink.Shared`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/ILLink.Tasks`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/ILLink.CodeFix`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/ILLink.RoslynAnalyzer`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/ILLink.RoslynAnalyzer/build`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/tools/illink/src/linker/ref`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/ILVerification`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/Common`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/inc`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/vm`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/aot`, include: \"topDirectoryOnly\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/aot/ILCompiler.DependencyAnalysisFramework`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/aot/ILCompiler.Diagnostics`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/coreclr/tools/aot/ILCompiler.TypeSystem`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/native/managed/cdac/Microsoft.Diagnostics.DataContractReader.Abstractions`, include: \"allDirectories\" }),");
        builder.AppendLine("    Transformer.sealSourceDirectory({ root: d`${repoRoot}/src/native/managed/cdac/gen`, include: \"allDirectories\" }),");
        builder.AppendLine("    f`${repoRoot}/src/coreclr/clr.featuredefines.props`,");
        foreach (string restoreInput in GetSharedRestoreInputs())
        {
            builder.AppendLine($"    {_paths.FileLiteral(restoreInput)},");
        }
        builder.AppendLine("];");
        builder.AppendLine("const nugetPackagesRoot = Environment.hasVariable(\"NUGET_PACKAGES\") ? Environment.getDirectoryValue(\"NUGET_PACKAGES\") : d`${Environment.getDirectoryValue(\"HOME\")}/.nuget/packages`;");
        builder.AppendLine("const nugetPackages = Transformer.sealSourceDirectory({ root: nugetPackagesRoot, include: \"allDirectories\" });");
        builder.AppendLine($"const nugetDataHome = {_paths.DirectoryLiteral(Path.Combine(_options.OutputDirectory, "nuget-data"))};");
        builder.AppendLine($"const nugetMigrationMarker = {_paths.FileLiteral(Path.Combine(_options.OutputDirectory, "nuget-data", "NuGet", "Migrations", "1"))};");
        builder.AppendLine("const repoGlobalJson = f`${repoRoot}/global.json`;");
        builder.AppendLine("const repoDirectoryBuildRsp = f`${repoRoot}/Directory.Build.rsp`;");
        builder.AppendLine();
        builder.AppendLine("function buildProject(project: File, workingDirectory: Directory, outputCache: Path, logDirectory: Directory, tempDirectory: Directory, dependencies: Transformer.InputArtifact[], dependencyCaches: Path[], exclusiveOutputDirectories: Directory[], sharedOutputDirectories: Directory[], outputFiles: Path[], inputDirectories: Directory[], properties: string[], targets: string[]): Transformer.ExecuteResult {");
        builder.AppendLine("    return Transformer.execute({");
        builder.AppendLine("        tool: dotnetWrapper,");
        builder.AppendLine("        workingDirectory,");
        builder.AppendLine("        arguments: [");
        builder.AppendLine("            Cmd.argument(Artifact.none(tempDirectory)),");
        builder.AppendLine("            Cmd.argument(Artifact.input(f`${repoRoot}/.dotnet/dotnet`)),");
        builder.AppendLine("            Cmd.argument(Artifact.input(msbuildDll)),");
        builder.AppendLine("            Cmd.argument(Artifact.input(project)),");
        builder.AppendLine("            Cmd.argument(\"/nologo\"),");
        builder.AppendLine("            Cmd.argument(\"/m:1\"),");
        builder.AppendLine("            Cmd.argument(\"/nodeReuse:false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:TrackFileAccess=false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:BuildProjectReferences=false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:MSBuildEnableWorkloadResolver=false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:UseSharedCompilation=false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:DisablePackageAssetsCache=true\"),");
        builder.AppendLine("            Cmd.argument(\"/p:EnableXlfLocalization=false\"),");
        builder.AppendLine("            Cmd.argument(\"/p:PackAsToolShimRuntimeIdentifiers=\"),");
        builder.AppendLine("            ...targets.map(t => Cmd.argument(\"/t:\" + t)),");
        builder.AppendLine("            ...properties.map(p => Cmd.argument(\"/p:\" + p)),");
        builder.AppendLine("        ],");
        builder.AppendLine("        dependencies,");
        builder.AppendLine("        tempDirectory,");
        builder.AppendLine("        outputs: [");
        builder.AppendLine("            { artifact: outputCache, existence: \"optional\" },");
        builder.AppendLine("            { directory: logDirectory, kind: \"exclusive\" },");
        builder.AppendLine("            ...outputFiles.map(f => <Transformer.FileOrPathOutput>{ artifact: f, existence: \"optional\" }),");
        builder.AppendLine("            ...exclusiveOutputDirectories.map(d => <Transformer.DirectoryOutput>{ directory: d, kind: \"exclusive\" }),");
        builder.AppendLine("            ...sharedOutputDirectories.map(d => <Transformer.DirectoryOutput>{ directory: d, kind: \"shared\" }),");
        builder.AppendLine("        ],");
        builder.AppendLine("        unsafe: {");
        builder.AppendLine("            allowPreservedOutputs: true,");
        builder.AppendLine("        },");
        builder.AppendLine("        environmentVariables: [");
        builder.AppendLine("            { name: \"DOTNET_HOST_PATH\", value: p`${repoRoot}/.dotnet/dotnet` },");
        builder.AppendLine("            { name: \"DOTNET_ROOT\", value: d`${repoRoot}/.dotnet` },");
        builder.AppendLine("            { name: \"NUGET_PACKAGES\", value: nugetPackagesRoot },");
        builder.AppendLine("            { name: \"XDG_DATA_HOME\", value: nugetDataHome },");
        builder.AppendLine("            { name: \"DOTNET_CLI_HOME\", value: tempDirectory },");
        builder.AppendLine("            { name: \"DOTNET_CLI_TELEMETRY_OPTOUT\", value: \"1\" },");
        builder.AppendLine("            { name: \"DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE\", value: \"1\" },");
        builder.AppendLine("            { name: \"DOTNET_EnableDiagnostics\", value: \"0\" },");
        builder.AppendLine("            { name: \"MSBUILDDEBUGPATH\", value: tempDirectory },");
        builder.AppendLine("            { name: \"DOTNET_NOLOGO\", value: \"1\" },");
        builder.AppendLine("            { name: \"DOTNET_SKIP_FIRST_TIME_EXPERIENCE\", value: \"1\" },");
        builder.AppendLine("            { name: \"HOME\", value: tempDirectory },");
        builder.AppendLine("        ],");
        builder.AppendLine("    });");
        builder.AppendLine("}");
        builder.AppendLine();

        foreach (NodeModel node in scheduledNodes)
        {
            builder.AppendLine($"const {node.Id}_inputs = [");
            foreach (string inputFile in node.InputFiles.Where(inputFile => !IsUnderAnyDirectory(inputFile, allOutputDirectories)))
            {
                builder.AppendLine($"    {_paths.FileLiteral(inputFile)},");
            }
            foreach (string inputDirectory in node.InputDirectories.Where(inputDirectory => !IsUnderAnyDirectory(inputDirectory, allOutputDirectories)))
            {
                builder.AppendLine($"    Transformer.sealSourceDirectory({{ root: {_paths.DirectoryLiteral(inputDirectory)}, include: \"allDirectories\" }}),");
            }
            builder.AppendLine("];");
            builder.AppendLine();
        }

        foreach (NodeModel node in scheduledNodes)
        {
            builder.AppendLine($"const {node.Id} = buildProject(");
            builder.AppendLine($"    {_paths.FileLiteral(node.ProjectPath)},");
            builder.AppendLine($"    {_paths.DirectoryLiteral(node.WorkingDirectory)},");
            builder.AppendLine($"    {_paths.PathLiteral(node.OutputCacheFile)},");
            builder.AppendLine($"    {_paths.DirectoryLiteral(node.LogDirectory)},");
            builder.AppendLine($"    {_paths.DirectoryLiteral(GetTempDirectory(node))},");
            builder.AppendLine("    [");
            builder.AppendLine("        dotnetRuntime,");
            builder.AppendLine("        repoEngineering,");
            builder.AppendLine("        ...repoSourceGenerators,");
            builder.AppendLine("        ...repoSharedBuildInputs,");
            builder.AppendLine("        nugetPackages,");
            builder.AppendLine("        nugetMigrationMarker,");
            builder.AppendLine("        repoGlobalJson,");
            builder.AppendLine("        repoDirectoryBuildRsp,");
            foreach (string dependency in effectiveDependencies[node.Id])
            {
                if (scheduledById.TryGetValue(dependency, out NodeModel? dependencyNode))
                {
                    builder.AppendLine($"        {dependency}.getOutputFile({_paths.PathLiteral(dependencyNode.OutputCacheFile)}),");
                    foreach (string dependencyOutputDirectory in dependencyNode.OutputDirectories)
                    {
                        builder.AppendLine($"        {dependency}.getOutputDirectory({_paths.DirectoryLiteral(dependencyOutputDirectory)}),");
                        foreach (string dependencyOutputFile in GetKnownOutputFiles(dependencyNode, dependencyOutputDirectory))
                        {
                            builder.AppendLine($"        {dependency}.getOutputFile({_paths.PathLiteral(dependencyOutputFile)}),");
                        }
                    }
                    foreach (string dependencyOutputFile in GetAdditionalKnownOutputFiles(dependencyNode))
                    {
                        builder.AppendLine($"        {dependency}.getOutputFile({_paths.PathLiteral(dependencyOutputFile)}),");
                    }
                }
            }
            builder.AppendLine($"        ...{node.Id}_inputs,");
            builder.AppendLine("    ],");
            builder.AppendLine("    [");
            foreach (string dependency in effectiveDependencies[node.Id])
            {
                if (scheduledById.TryGetValue(dependency, out NodeModel? dependencyNode))
                {
                    builder.AppendLine($"        {_paths.PathLiteral(dependencyNode.OutputCacheFile)},");
                }
            }
            builder.AppendLine("    ],");
            builder.AppendLine("    [");
            foreach (string outputDirectory in node.OutputDirectories.Where(outputDirectory => !sharedOutputDirectories.Contains(RepoPaths.Normalize(outputDirectory))))
            {
                builder.AppendLine($"        {_paths.DirectoryLiteral(outputDirectory)},");
            }
            builder.AppendLine("    ],");
            builder.AppendLine("    [");
            foreach (string outputDirectory in node.OutputDirectories.Where(outputDirectory => sharedOutputDirectories.Contains(RepoPaths.Normalize(outputDirectory))))
            {
                builder.AppendLine($"        {_paths.DirectoryLiteral(outputDirectory)},");
            }
            builder.AppendLine("    ],");
            builder.AppendLine("    [");
            foreach (string outputFile in node.OutputDirectories.SelectMany(outputDirectory => GetKnownOutputFiles(node, outputDirectory)))
            {
                builder.AppendLine($"        {_paths.PathLiteral(outputFile)},");
            }
            foreach (string outputFile in GetAdditionalKnownOutputFiles(node))
            {
                builder.AppendLine($"        {_paths.PathLiteral(outputFile)},");
            }
            builder.AppendLine("    ],");
            builder.AppendLine("    [],");
            builder.AppendLine("    [");
            foreach ((string key, string value) in node.GlobalProperties)
            {
                if (!string.Equals(key, "CurrentSolutionConfigurationContents", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine($"        \"{key}={value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\",");
                }
            }
            if (ShouldDisableAppHost(node))
            {
                builder.AppendLine("        \"UseAppHost=false\",");
            }
            builder.AppendLine("    ],");
            builder.AppendLine("    [");
            foreach (string target in node.Targets)
            {
                builder.AppendLine($"        \"{target}\",");
            }
            builder.AppendLine("    ]);");
            builder.AppendLine();
        }

        builder.AppendLine("@@public");
        builder.AppendLine($"export const result = [{string.Join(", ", scheduledNodes.Select(n => n.Id))}];");
        return builder.ToString();
    }

    private static string GetTempDirectory(NodeModel node)
    {
        string nodeCacheDirectory = Path.GetDirectoryName(node.OutputCacheFile)!;
        string cacheDirectory = Path.GetDirectoryName(nodeCacheDirectory)!;
        string outputDirectory = Path.GetDirectoryName(cacheDirectory)!;
        return Path.Combine(outputDirectory, "temp", node.Id);
    }

    private static bool IsTraversalProject(NodeModel node)
    {
        using var reader = new StreamReader(node.ProjectPath);
        for (int i = 0; i < 5 && !reader.EndOfStream; i++)
        {
            string? line = reader.ReadLine();
            if (line?.Contains("Sdk=\"Microsoft.Build.Traversal\"", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static string[] GetEffectiveDependencies(NodeModel node, string[] toolDependencyIds)
    {
        bool buildsILLinkTooling =
            node.RelativePath.StartsWith("src/tools/illink/", StringComparison.OrdinalIgnoreCase) ||
            node.RelativePath.StartsWith("src/libraries/System.Runtime.InteropServices/gen/", StringComparison.OrdinalIgnoreCase);

        return node.Dependencies
            .Concat(buildsILLinkTooling
                ? Array.Empty<string>()
                : toolDependencyIds.Where(toolDependencyId => !string.Equals(toolDependencyId, node.Id, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsOuterCrossTargetingProject(NodeModel node)
    {
        if (node.GlobalProperties.ContainsKey("TargetFramework"))
        {
            return false;
        }

        using var reader = new StreamReader(node.ProjectPath);
        for (int i = 0; i < 80 && !reader.EndOfStream; i++)
        {
            string? line = reader.ReadLine();
            if (line?.Contains("<TargetFrameworks>", StringComparison.OrdinalIgnoreCase) == true ||
                line?.Contains("TargetFrameworks=", StringComparison.OrdinalIgnoreCase) == true)
            {
                return true;
            }
        }

        return false;
    }

    private static NodeModel[] TopologicalSort(NodeModel[] nodes, Dictionary<string, NodeModel> nodesById, Dictionary<string, string[]> effectiveDependencies)
    {
        var result = new List<NodeModel>();
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (NodeModel node in nodes)
        {
            Visit(node);
        }

        return result.ToArray();

        void Visit(NodeModel node)
        {
            if (!visited.Add(node.Id))
            {
                return;
            }

            if (!visiting.Add(node.Id))
            {
                return;
            }

            foreach (string dependency in effectiveDependencies[node.Id])
            {
                if (nodesById.TryGetValue(dependency, out NodeModel? dependencyNode))
                {
                    Visit(dependencyNode);
                }
            }

            visiting.Remove(node.Id);
            result.Add(node);
        }
    }

    private IEnumerable<string> GetSharedRestoreInputs()
    {
        string artifactsObj = Path.Combine(_options.RepoRoot, "artifacts", "obj");
        if (!Directory.Exists(artifactsObj))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(artifactsObj, "*.nuget.g.props", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(artifactsObj, "*.nuget.g.targets", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(artifactsObj, "project.assets.json", SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static IEnumerable<string> GetKnownOutputFiles(NodeModel node, string outputDirectory)
    {
        if (node.RelativePath.Equals("src/tools/illink/src/ILLink.Tasks/ILLink.Tasks.csproj", StringComparison.OrdinalIgnoreCase) &&
            outputDirectory.EndsWith($"{Path.DirectorySeparatorChar}ILLink.Tasks{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}net", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(outputDirectory, "ILLink.Tasks.dll");
            yield return Path.Combine(outputDirectory, "illink.deps.json");
            yield return Path.Combine(outputDirectory, "illink.runtimeconfig.json");
        }
    }

    private IEnumerable<string> GetAdditionalKnownOutputFiles(NodeModel node)
    {
        string assemblyName = Path.GetFileNameWithoutExtension(node.ProjectPath);
        string coreClrBin = Path.Combine(_options.RepoRoot, "artifacts", "bin", "coreclr", $"linux.x64.{_options.Configuration}");
        if (node.RelativePath.StartsWith("src/coreclr/", StringComparison.OrdinalIgnoreCase) &&
            !node.RelativePath.StartsWith("src/coreclr/nativeaot/", StringComparison.OrdinalIgnoreCase) &&
            node.RelativePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(coreClrBin, $"{assemblyName}.deps.json");
            yield return Path.Combine(coreClrBin, $"{assemblyName}.dll");
            yield return Path.Combine(coreClrBin, $"{assemblyName}.pdb");
            yield return Path.Combine(coreClrBin, $"{assemblyName}.runtimeconfig.json");
            yield return Path.Combine(coreClrBin, "PDB", $"{assemblyName}.pdb");
        }

        if (node.RelativePath.Equals("src/libraries/System.Runtime.InteropServices/gen/Microsoft.Interop.SourceGeneration/Microsoft.Interop.SourceGeneration.csproj", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(_options.RepoRoot, "artifacts", "bin", "microsoft.netcore.app.ref", "analyzers", "dotnet", "cs", "Microsoft.Interop.SourceGeneration.dll");
            yield return Path.Combine(_options.RepoRoot, "artifacts", "bin", "microsoft.netcore.app.ref", "analyzers", "dotnet", "cs", "Microsoft.Interop.SourceGeneration.pdb");
        }

        if (node.RelativePath.Equals("src/libraries/System.Runtime.InteropServices/gen/LibraryImportGenerator/LibraryImportGenerator.csproj", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(_options.RepoRoot, "artifacts", "bin", "microsoft.netcore.app.ref", "analyzers", "dotnet", "cs", "Microsoft.Interop.LibraryImportGenerator.dll");
            yield return Path.Combine(_options.RepoRoot, "artifacts", "bin", "microsoft.netcore.app.ref", "analyzers", "dotnet", "cs", "Microsoft.Interop.LibraryImportGenerator.pdb");
        }

        if (node.RelativePath.Equals("src/libraries/System.Private.CoreLib/ref/System.Private.CoreLib.csproj", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string fileName in GetNetCoreAppRefAnalyzerFiles())
            {
                yield return Path.Combine(_options.RepoRoot, "artifacts", "bin", "microsoft.netcore.app.ref", "analyzers", "dotnet", "cs", fileName);
            }
        }
    }

    private static bool ShouldDisableAppHost(NodeModel node) =>
        node.RelativePath is
            "src/coreclr/tools/AssemblyChecker/AssemblyChecker.csproj" or
            "src/coreclr/tools/PdbChecker/PdbChecker.csproj" or
            "src/coreclr/tools/SuperFileCheck/SuperFileCheck.csproj" or
            "src/coreclr/tools/ILVerification/ILVerification.csproj" or
            "src/coreclr/tools/ILVerify/ILVerify.csproj" or
            "src/coreclr/tools/ILTrim/ILTrim.csproj" or
            "src/coreclr/tools/ILTrim.Core/ILTrim.Core.csproj" or
            "src/coreclr/tools/dotnet-pgo/dotnet-pgo.csproj" or
            "src/coreclr/tools/r2rdump/R2RDump.csproj" or
            "src/coreclr/tools/r2rtest/R2RTest.csproj" or
            "src/coreclr/tools/runincontext/runincontext.csproj" or
            "src/coreclr/tools/tieringtest/tieringtest.csproj" or
            "src/coreclr/tools/cdac-build-tool/cdac-build-tool.csproj" or
            "src/coreclr/tools/aot/ILCompiler/repro/repro.csproj" or
            "src/tools/StressLogAnalyzer/src/StressLogAnalyzer.csproj";

    private static IEnumerable<string> GetNetCoreAppRefAnalyzerFiles()
    {
        yield return "Microsoft.Extensions.Logging.Generators.dll";
        yield return "Microsoft.Extensions.Logging.Generators.pdb";
        yield return "Microsoft.Extensions.Options.SourceGeneration.dll";
        yield return "Microsoft.Extensions.Options.SourceGeneration.pdb";
        yield return "Microsoft.Interop.ComInterfaceGenerator.dll";
        yield return "Microsoft.Interop.ComInterfaceGenerator.pdb";
        yield return "Microsoft.Interop.JavaScript.JSImportGenerator.dll";
        yield return "Microsoft.Interop.JavaScript.JSImportGenerator.pdb";
        yield return "System.Text.Json.SourceGeneration.dll";
        yield return "System.Text.Json.SourceGeneration.pdb";
        yield return "System.Text.RegularExpressions.Generator.dll";
        yield return "System.Text.RegularExpressions.Generator.pdb";
    }

    private static bool IsUnderAnyDirectory(string path, string[] directories)
    {
        string normalizedPath = RepoPaths.Normalize(path);
        return directories.Any(directory =>
            normalizedPath.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }
}
