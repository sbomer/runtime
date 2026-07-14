// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Execution;

public interface IProjectGraphPolicy
{
    Dictionary<string, string> CreateGlobalProperties();

    IEnumerable<string> GetAdditionalInputFiles(ProjectInstance project, string relativePath);

    IEnumerable<string> GetAdditionalInputDirectories(ProjectInstance project, string relativePath);

    IEnumerable<string> GetAdditionalOutputDirectories(ProjectInstance project, string relativePath);

    bool ShouldSkipOutputDirectory(string relativePath, string directory);

    bool ShouldSkipProjectDirectoryInput(string projectDirectory);
}
