// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

internal enum ProjectReferenceCaptureOperation
{
    Add,
    Configure,
    Remove,
    Update,
}

public sealed class PrepareProjectReferenceCapture : Task
{
    public ITaskItem[]? EvaluationProjectReferences { get; set; }

    public ITaskItem[]? ResolvedProjectReferences { get; set; }

    public ITaskItem[]? NegotiatedProjectReferences { get; set; }

    [Output]
    public ITaskItem[] PreparedProjectReferences { get; private set; } = [];

    public override bool Execute()
    {
        SortedDictionary<string, ITaskItem> evaluationProjectReferences = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem projectReference in EvaluationProjectReferences ?? [])
        {
            if (ShouldIgnoreReference(projectReference))
            {
                continue;
            }

            evaluationProjectReferences.TryAdd(projectReference.GetMetadata("FullPath"), projectReference);
        }

        Dictionary<string, ITaskItem> negotiatedProjectReferences = new(StringComparer.OrdinalIgnoreCase);
        foreach (ITaskItem projectReference in NegotiatedProjectReferences ?? [])
        {
            negotiatedProjectReferences.TryAdd(projectReference.GetMetadata("FullPath"), projectReference);
        }

        ITaskItem[] resolvedProjectReferences = ResolvedProjectReferences ?? [];
        HashSet<string> finalProjectReferences = new(StringComparer.OrdinalIgnoreCase);
        List<ITaskItem> preparedProjectReferences = new(resolvedProjectReferences.Length + evaluationProjectReferences.Count);
        foreach (ITaskItem projectReference in resolvedProjectReferences)
        {
            if (ShouldIgnoreReference(projectReference))
            {
                continue;
            }

            string fullPath = projectReference.GetMetadata("FullPath");
            finalProjectReferences.Add(fullPath);

            TaskItem preparedProjectReference = new(projectReference);
            if (negotiatedProjectReferences.TryGetValue(fullPath, out ITaskItem? negotiatedProjectReference))
            {
                preparedProjectReference.SetMetadata(
                    "SetTargetFramework",
                    negotiatedProjectReference.GetMetadata("SetTargetFramework"));
            }

            ProjectReferenceCaptureOperation operation = evaluationProjectReferences.ContainsKey(fullPath)
                ? ProjectReferenceCaptureOperation.Update
                : ProjectReferenceCaptureOperation.Add;
            preparedProjectReference.SetMetadata("CaptureOperation", operation.ToString());

            preparedProjectReferences.Add(preparedProjectReference);
        }

        foreach ((string fullPath, ITaskItem projectReference) in evaluationProjectReferences)
        {
            if (finalProjectReferences.Contains(fullPath))
            {
                continue;
            }

            TaskItem preparedProjectReference = new(projectReference);
            preparedProjectReference.SetMetadata("SetTargetFramework", "");
            preparedProjectReference.SetMetadata("CaptureOperation", ProjectReferenceCaptureOperation.Remove.ToString());
            preparedProjectReferences.Add(preparedProjectReference);
        }

        PreparedProjectReferences = preparedProjectReferences.ToArray();
        return true;
    }

    private static bool ShouldIgnoreReference(ITaskItem projectReference) =>
        string.Equals(projectReference.GetMetadata("BuildReference"), "false", StringComparison.OrdinalIgnoreCase);
}
