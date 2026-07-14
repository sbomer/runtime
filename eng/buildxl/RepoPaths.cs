// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public sealed class RepoPaths
{
    private readonly string _repoRoot;

    public RepoPaths(string repoRoot)
    {
        _repoRoot = Normalize(repoRoot);
    }

    public string RepoRoot => _repoRoot;

    public string Relative(string path) => Path.GetRelativePath(_repoRoot, path).Replace('\\', '/');

    public string DirectoryLiteral(string path)
    {
        string relative = ToRepoRelative(path);
        return string.IsNullOrEmpty(relative) ? "repoRoot" : $"d`${{repoRoot}}/{DPath(relative)}`";
    }

    public string FileLiteral(string path)
    {
        string relative = ToRepoRelative(path);
        return string.IsNullOrEmpty(relative) ? throw new InvalidOperationException("Repo root cannot be a file.") : $"f`${{repoRoot}}/{DPath(relative)}`";
    }

    public string PathLiteral(string path)
    {
        string relative = ToRepoRelative(path);
        return string.IsNullOrEmpty(relative) ? "repoRoot" : $"p`${{repoRoot}}/{DPath(relative)}`";
    }

    public static string FullPath(string path, string baseDirectory)
    {
        path = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(path, Path.IsPathRooted(path) ? Path.GetPathRoot(path)! : baseDirectory).TrimEnd(Path.DirectorySeparatorChar);
    }

    public static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

    public static string DPath(string path) => path.Replace('\\', '/').Replace("`", "\\`");

    private string ToRepoRelative(string path)
    {
        string fullPath = Normalize(path);
        if (!fullPath.Equals(_repoRoot, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(_repoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Path '{path}' is outside repo root '{_repoRoot}'.");
        }

        string relative = Path.GetRelativePath(_repoRoot, fullPath).Replace('\\', '/');
        return relative == "." ? string.Empty : relative;
    }
}
