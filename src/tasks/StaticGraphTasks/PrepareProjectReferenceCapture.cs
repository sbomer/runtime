// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

public sealed class PrepareProjectReferenceCapture : Task
{
    // ResolveP2PReferences adds or modifies this negotiation metadata on the ProjectReference items it replaces.
    private static readonly HashSet<string> s_resolveP2PGeneratedMetadata = new(StringComparer.OrdinalIgnoreCase)
    {
        "AdditionalPropertiesFromProject",
        "HasSingleTargetFramework",
        "IsRidAgnostic",
        "IsVcxOrNativeProj",
        "MSBuildSourceProjectFile",
        "MSBuildSourceTargetName",
        "NearestTargetFramework",
        "OriginalItemSpec",
        "Platform",
        "Platforms",
        "SetTargetFramework",
        "SkipGetTargetFrameworkProperties",
        "TargetFrameworkMonikers",
        "TargetFrameworks",
        "TargetPlatformMonikers",
        "UndefineProperties",
    };

    public ITaskItem[]? EvaluationProjectReferences { get; set; }

    public ITaskItem[]? ResolvedProjectReferences { get; set; }

    [Output]
    public ITaskItem[] PreparedProjectReferences { get; private set; } = [];

    public override bool Execute()
    {
        HashSet<string> evaluationProjectReferences = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem projectReference in EvaluationProjectReferences ?? [])
        {
            evaluationProjectReferences.Add(projectReference.GetMetadata("FullPath"));
        }

        ITaskItem[] resolvedProjectReferences = ResolvedProjectReferences ?? [];
        List<ITaskItem> preparedProjectReferences = new(resolvedProjectReferences.Length);
        foreach (ITaskItem projectReference in resolvedProjectReferences)
        {
            TaskItem preparedProjectReference = new(projectReference);
            bool isDynamicallyAdded = !evaluationProjectReferences.Contains(projectReference.GetMetadata("FullPath"));
            preparedProjectReference.SetMetadata("CaptureIsDynamicallyAdded", isDynamicallyAdded.ToString());

            if (isDynamicallyAdded)
            {
                SortedDictionary<string, string> metadata = new(StringComparer.OrdinalIgnoreCase);
                foreach (DictionaryEntry entry in projectReference.CloneCustomMetadata())
                {
                    string name = (string)entry.Key;
                    string value = entry.Value?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(value) && !s_resolveP2PGeneratedMetadata.Contains(name))
                    {
                        metadata.Add(name, value);
                    }
                }

                string serializedMetadata = JsonSerializer.Serialize(metadata);
                preparedProjectReference.SetMetadata(
                    "CaptureProjectReferenceMetadata",
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedMetadata)));
            }

            preparedProjectReferences.Add(preparedProjectReference);
        }

        PreparedProjectReferences = preparedProjectReferences.ToArray();
        return true;
    }
}
