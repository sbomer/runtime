// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Utilities;
using Xunit;

namespace Microsoft.DotNet.Runtime.Tasks.Tests;

public sealed class DeduplicateProjectReferenceCaptureTests
{
    [Fact]
    public void DeduplicatesByReplayMetadata()
    {
        TaskItem first = CreateItem("net8.0");
        TaskItem duplicate = CreateItem("net8.0");
        TaskItem differentlyCasedDuplicate = CreateItem("NET8.0");
        TaskItem differentFramework = CreateItem("net9.0");

        DeduplicateProjectReferenceCapture task = new()
        {
            Items = [first, duplicate, differentlyCasedDuplicate, differentFramework],
        };

        Assert.True(task.Execute());
        Assert.Equal([first, differentFramework], task.UniqueItems);
    }

    private static TaskItem CreateItem(string targetFramework)
    {
        TaskItem item = new("capture");
        item.SetMetadata("CaptureParentProject", "src/Parent.proj");
        item.SetMetadata("CaptureParentTargetFramework", "net8.0");
        item.SetMetadata("CaptureReferencedProject", "src/Child.csproj");
        item.SetMetadata("CaptureSetTargetFramework", $"TargetFramework={targetFramework}");
        return item;
    }
}
