// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

public sealed record NodeModel(
    string Id,
    string ProjectPath,
    string RelativePath,
    string WorkingDirectory,
    string[] Targets,
    Dictionary<string, string> GlobalProperties,
    string[] Dependencies,
    string[] OutputDirectories,
    string[] InputFiles,
    string[] InputDirectories,
    string OutputCacheFile,
    string LogDirectory);
