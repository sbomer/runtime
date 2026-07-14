// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Build.Locator;

GeneratorOptions options = GeneratorOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);
string nugetMigrationsDirectory = Path.Combine(options.OutputDirectory, "nuget-data", "NuGet", "Migrations");
Directory.CreateDirectory(nugetMigrationsDirectory);
File.WriteAllText(Path.Combine(nugetMigrationsDirectory, "1"), string.Empty);
WriteDotNetWrapper(Path.Combine(options.OutputDirectory, "dotnet-msbuild-wrapper.sh"));

if (!MSBuildLocator.IsRegistered)
{
    MSBuildLocator.RegisterMSBuildPath(options.SdkPath);
}

static void WriteDotNetWrapper(string path)
{
    File.WriteAllText(path, """
#!/usr/bin/env bash
set -euo pipefail
export TMPDIR="$1"
shift
dotnet_exe="$1"
shift
exec "$dotnet_exe" "$@"
""");

    if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
    {
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}

RunGenerator(options);

[MethodImpl(MethodImplOptions.NoInlining)]
static void RunGenerator(GeneratorOptions options)
{
    var paths = new RepoPaths(options.RepoRoot);
    var policy = new RuntimeProjectGraphPolicy(options);
    var ownership = new OutputOwnership(paths, policy);
    var loader = new MsBuildGraphLoader(options, paths, policy, ownership);

    NodeModel[] nodes = loader.LoadNodes();
    nodes = ownership.RemoveDuplicateOutputOwners(nodes);
    nodes = AddRuntimeDependencyForCoreHost(nodes);
    OutputCollision[] collisions = ownership.FindCollisions(nodes);

    var model = new GraphModel(options.Subset, options.Configuration, nodes, collisions);
    var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    File.WriteAllText(Path.Combine(options.OutputDirectory, "graph.json"), JsonSerializer.Serialize(model, jsonOptions));
    File.WriteAllText(Path.Combine(options.OutputDirectory, "projects.dsc"), new DScriptWriter(paths, options).Generate(model));
    File.WriteAllText(Path.Combine(options.OutputDirectory, "config.dsc"), ConfigWriter.Generate(options.OutputRelativeRoot));

    Console.WriteLine($"Generated {nodes.Length} nodes to {options.OutputDirectory}");
    Console.WriteLine($"Output directory collisions: {collisions.Length}");
    foreach (OutputCollision collision in collisions.Take(20))
    {
        Console.WriteLine($"  {collision.Directory}");
        foreach (string id in collision.NodeIds)
        {
            Console.WriteLine($"    {id}");
        }
    }

    static NodeModel[] AddRuntimeDependencyForCoreHost(NodeModel[] nodes)
    {
        NodeModel? runtime = nodes.FirstOrDefault(node => node.RelativePath.Equals("src/coreclr/runtime.proj", StringComparison.OrdinalIgnoreCase));
        if (runtime is null)
        {
            return nodes;
        }

        return nodes
            .Select(node =>
                node.RelativePath.Equals("src/native/corehost/corehost.proj", StringComparison.OrdinalIgnoreCase)
                    ? node with { Dependencies = node.Dependencies.Append(runtime.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() }
                    : node)
            .ToArray();
    }
}
