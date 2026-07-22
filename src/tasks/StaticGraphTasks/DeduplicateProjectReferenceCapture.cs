// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Runtime.Tasks;

public sealed class DeduplicateProjectReferenceCapture : Task
{
    [Required]
    public ITaskItem[]? Items { get; set; }

    [Output]
    public ITaskItem[] UniqueItems { get; private set; } = [];

    public override bool Execute()
    {
        HashSet<CaptureKey> keys = new(CaptureKeyComparer.Instance);
        List<ITaskItem> uniqueItems = new(Items!.Length);

        foreach (ITaskItem item in Items)
        {
            if (keys.Add(new CaptureKey(item)))
            {
                uniqueItems.Add(item);
            }
        }

        UniqueItems = uniqueItems.ToArray();
        return true;
    }

    private sealed class CaptureKeyComparer : IEqualityComparer<CaptureKey>
    {
        public static CaptureKeyComparer Instance { get; } = new();

        public bool Equals(CaptureKey x, CaptureKey y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.ParentProject, y.ParentProject) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.ParentTargetFramework, y.ParentTargetFramework) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.ReferencedProject, y.ReferencedProject) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.SetTargetFramework, y.SetTargetFramework) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Operation, y.Operation);

        public int GetHashCode(CaptureKey obj)
        {
            HashCode hashCode = default;
            hashCode.Add(obj.ParentProject, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(obj.ParentTargetFramework, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(obj.ReferencedProject, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(obj.SetTargetFramework, StringComparer.OrdinalIgnoreCase);
            hashCode.Add(obj.Operation, StringComparer.OrdinalIgnoreCase);
            return hashCode.ToHashCode();
        }
    }

    private readonly record struct CaptureKey(
        string ParentProject,
        string ParentTargetFramework,
        string ReferencedProject,
        string SetTargetFramework,
        string Operation)
    {
        public CaptureKey(ITaskItem item)
            : this(
                item.GetMetadata("CaptureParentProject"),
                item.GetMetadata("CaptureParentTargetFramework"),
                item.GetMetadata("CaptureReferencedProject"),
                item.GetMetadata("CaptureSetTargetFramework"),
                item.GetMetadata("CaptureOperation"))
        {
        }
    }
}
