// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public static class ConfigWriter
{
    public static string Generate(string outputRelativeRoot)
    {
        string repoRootFromOutputDirectory = RelativeToRepoRoot(outputRelativeRoot);

        return $$"""
config({
    mounts: [
        {
            name: a`RuntimeRepo`,
            path: d`{{RepoPaths.DPath(repoRootFromOutputDirectory)}}`,
            isReadable: true,
            isWritable: true,
            isScrubbable: false,
            trackSourceFileChanges: true
        },
        {
            name: a`GeneratedMsBuildGraph`,
            path: d`.`,
            isReadable: true,
            isWritable: true,
            isScrubbable: false,
            trackSourceFileChanges: true
        }
    ],
    resolvers: [
        {
            kind: "DScript",
            modules: [
                {
                    moduleName: "RuntimeGeneratedMsBuild",
                    projects: [f`projects.dsc`]
                }
            ]
        }
    ]
});
""";
    }

    private static string RelativeToRepoRoot(string outputRelativeRoot)
    {
        string normalized = outputRelativeRoot.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized) || normalized == ".")
        {
            return ".";
        }

        int segmentCount = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Length;
        return string.Join('/', Enumerable.Repeat("..", segmentCount));
    }
}
