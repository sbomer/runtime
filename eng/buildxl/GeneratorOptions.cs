// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public sealed record GeneratorOptions(
    string RepoRoot,
    string Subset,
    string Configuration,
    string OutputDirectory,
    string OutputRelativeRoot,
    string SdkPath)
{
    public static GeneratorOptions Parse(string[] args)
    {
        string repoRoot = Path.GetFullPath(GetArg(args, "--repoRoot", Directory.GetCurrentDirectory()));
        string subset = GetArg(args, "--subset", "clr");
        string configuration = GetArg(args, "--configuration", "Debug");
        string outputDirectory = Path.GetFullPath(GetArg(args, "--outputDir", Path.Combine(repoRoot, "artifacts", "obj", "buildxl-dscript")));
        string outputRelativeRoot = GetArg(args, "--outputRelativeRoot", Path.GetRelativePath(repoRoot, outputDirectory));
        string sdkPath = Path.Combine(repoRoot, ".dotnet", "sdk", "11.0.100-preview.5.26227.104");

        return new GeneratorOptions(repoRoot, subset, configuration, outputDirectory, outputRelativeRoot, sdkPath);
    }

    private static string GetArg(string[] args, string name, string defaultValue)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(args[i], name))
            {
                return args[i + 1];
            }
        }

        return defaultValue;
    }
}
