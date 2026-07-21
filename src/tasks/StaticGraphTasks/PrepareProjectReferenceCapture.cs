// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

public sealed class PrepareProjectReferenceCapture : Task
{
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

            preparedProjectReferences.Add(preparedProjectReference);
        }

        PreparedProjectReferences = preparedProjectReferences.ToArray();
        return true;
    }
}
