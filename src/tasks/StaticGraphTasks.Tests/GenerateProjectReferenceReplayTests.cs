// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;

namespace Microsoft.DotNet.Runtime.Tasks.Tests;

public sealed class GenerateProjectReferenceReplayTests
{
    [Fact]
    public void GeneratesNodeAndAddedEdgeReplay()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllLines(captureFile,
            [
                "src/Parent.proj|net8.0|||Configure",
                "src/Parent.proj|net8.0|src/Child.csproj|TargetFramework=net8.0|Update",
                "src/Parent.proj|net8.0|src/Added.csproj|TargetFramework=net8.0|Add",
                "src/Other.proj|net9.0|src/Child.csproj|TargetFramework=net9.0|Update",
            ]);

            TestBuildEngine buildEngine = new();
            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = buildEngine,
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());
            Assert.Empty(buildEngine.Errors);

            string dispatcherText = File.ReadAllText(replayFile);
            Assert.DoesNotContain("<?xml", dispatcherText, StringComparison.Ordinal);

            XDocument dispatcher = XDocument.Load(replayFile);
            XElement import = Assert.Single(dispatcher.Descendants("Import"));
            Assert.Contains("$(_ProjectReferenceReplayProject).targets", import.Attribute("Project")!.Value);

            XDocument nodeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Child.csproj"));
            string nodeReplayText = File.ReadAllText(GetReplayFile(replayFile, "src/Child.csproj"));
            Assert.DoesNotContain("<?xml", nodeReplayText, StringComparison.Ordinal);
            Assert.DoesNotContain("Restrict outer builds", nodeReplayText, StringComparison.Ordinal);
            Assert.DoesNotContain("Replay selected target frameworks", nodeReplayText, StringComparison.Ordinal);
            XElement[] targetFrameworks = nodeReplay.Descendants("TargetFrameworks").ToArray();
            XElement targetFramework = Assert.Single(targetFrameworks);
            Assert.Equal("net8.0;net9.0", targetFramework.Value);
            Assert.Equal("'$(TargetFramework)' == ''", targetFramework.Parent!.Attribute("Condition")!.Value);

            XDocument edgeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.proj"));
            Assert.Empty(edgeReplay.Descendants("Target"));
            XElement replayedReference = Assert.Single(
                edgeReplay.Descendants("ProjectReference"),
                element => element.Attribute("Include") is not null);
            Assert.Equal("$(RepoRoot)src/Added.csproj", replayedReference.Attribute("Include")!.Value);
            Assert.Null(replayedReference.Attribute("Update"));
            Assert.Null(replayedReference.Element("SkipGetTargetFrameworkProperties"));
            Assert.Equal("TargetFramework=net8.0", replayedReference.Element("ProjectReferenceReplaySetTargetFramework")!.Value);
            Assert.Null(replayedReference.Element("SetTargetFramework"));
            Assert.Null(replayedReference.Element("UndefineProperties"));
            Assert.Empty(edgeReplay.Descendants("_ReplayProjectReference"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void DoesNotUpdateExistingReferenceWithInferredFramework()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllLines(captureFile,
            [
                "src/Parent.csproj|net8.0|src/Child.csproj||Update",
                "src/Other.csproj|net8.0|src/Child.csproj|TargetFramework=net8.0|Update",
            ]);

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            Assert.False(File.Exists(GetReplayFile(replayFile, "src/Parent.csproj")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MaterializesDynamicallyAddedReference()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(
                captureFile,
                "src/Parent.csproj|net8.0|src/Child.csproj|TargetFramework=net8.0|Add");

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            XDocument replay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.csproj"));
            XElement disableTransitiveReferences = Assert.Single(replay.Descendants("DisableTransitiveProjectReferences"));
            Assert.Equal("true", disableTransitiveReferences.Value);
            Assert.Equal("'$(TargetFramework)' == 'net8.0'", disableTransitiveReferences.Parent!.Attribute("Condition")!.Value);

            XElement replayedReference = Assert.Single(
                replay.Descendants("ProjectReference"),
                element => element.Attribute("Include") is not null);
            Assert.Equal("$(RepoRoot)src/Child.csproj", replayedReference.Attribute("Include")!.Value);
            Assert.Null(replayedReference.Attribute("Update"));
            Assert.Equal("TargetFramework=net8.0", replayedReference.Element("SetTargetFramework")!.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ClassifiesProjectReferenceDelta()
    {
        TaskItem evaluationReference = new("src/Existing.csproj");
        TaskItem removedEvaluationReference = new("src/Removed.csproj");
        TaskItem resolvedReference = new("src/Existing.csproj");
        TaskItem removedResolvedReference = new("src/Removed.csproj");
        removedResolvedReference.SetMetadata("BuildReference", "false");
        TaskItem dynamicallyAddedReference = new("src/Added.csproj");
        dynamicallyAddedReference.SetMetadata("SetTargetFramework", "TargetFramework=net8.0");

        PrepareProjectReferenceCapture task = new()
        {
            EvaluationProjectReferences = [evaluationReference, removedEvaluationReference],
            ResolvedProjectReferences = [resolvedReference, removedResolvedReference, dynamicallyAddedReference],
        };

        Assert.True(task.Execute());

        Assert.Equal("Update", task.PreparedProjectReferences[0].GetMetadata("CaptureOperation"));

        ITaskItem preparedDynamicReference = task.PreparedProjectReferences[1];
        Assert.Equal("Add", preparedDynamicReference.GetMetadata("CaptureOperation"));
        Assert.Equal("TargetFramework=net8.0", preparedDynamicReference.GetMetadata("SetTargetFramework"));

        ITaskItem preparedRemovedReference = task.PreparedProjectReferences[2];
        Assert.Equal("Remove", preparedRemovedReference.GetMetadata("CaptureOperation"));
        Assert.Equal(removedEvaluationReference.GetMetadata("FullPath"), preparedRemovedReference.GetMetadata("FullPath"));
    }

    [Fact]
    public void DoesNotReplayMetadataForReferenceWithoutNegotiation()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(captureFile, "src/Parent.csproj|net8.0|src/Child.csproj||Update");

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            Assert.False(File.Exists(GetReplayFile(replayFile, "src/Parent.csproj")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RemovesReferenceRemovedAfterEvaluation()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(captureFile, "src/Parent.proj|net8.0|src/Removed.csproj||Remove");

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            XDocument edgeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.proj"));
            XElement removedReference = Assert.Single(
                edgeReplay.Descendants("ProjectReference"),
                element => element.Attribute("Remove") is not null);
            Assert.Equal("$(RepoRoot)src/Removed.csproj", removedReference.Attribute("Remove")!.Value);
            Assert.Empty(edgeReplay.Descendants("_ReplayProjectReference"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("/rooted/Parent.proj|net8.0|||Configure")]
    [InlineData("../Parent.proj|net8.0|||Configure")]
    [InlineData("src/Parent.proj|net'8.0|||Configure")]
    [InlineData("src/Parent.proj|net'8.0|src/Child.csproj|TargetFramework=net8.0|Update")]
    [InlineData("src/Parent.proj|net8.0|src/Child.csproj||Invalid")]
    public void RejectsInvalidConditionInputs(string record)
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(captureFile, record);

            TestBuildEngine buildEngine = new();
            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = buildEngine,
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.False(task.Execute());
            Assert.NotEmpty(buildEngine.Errors);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"StaticGraphTasks.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string GetReplayFile(string replayFile, string projectPath) =>
        Path.Combine(
            $"{replayFile}.d",
            $"{projectPath}.targets".Replace('/', Path.DirectorySeparatorChar));

    private sealed class TestBuildEngine : IBuildEngine
    {
        public List<BuildErrorEventArgs> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => string.Empty;

        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotSupportedException();

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
