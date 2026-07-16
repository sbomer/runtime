// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Build.Framework;
using Xunit;

namespace Microsoft.DotNet.Runtime.Tasks.Tests;

public sealed class GenerateProjectReferenceReplayTests
{
    [Fact]
    public void GeneratesNodeAndEdgeReplay()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllLines(captureFile,
            [
                "src/Parent.proj|net8.0||",
                "src/Parent.proj|net8.0|src/Child.csproj|TargetFramework=net8.0",
                "src/Other.proj|net9.0|src/Child.csproj|TargetFramework=net9.0",
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

            XDocument dispatcher = XDocument.Load(replayFile);
            XElement import = Assert.Single(dispatcher.Descendants("Import"));
            Assert.Contains("$(_ProjectReferenceReplayProject).targets", import.Attribute("Project")!.Value);

            XDocument nodeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Child.csproj"));
            XElement[] targetFrameworks = nodeReplay.Descendants("TargetFrameworks").ToArray();
            XElement targetFramework = Assert.Single(targetFrameworks);
            Assert.Equal("net8.0;net9.0", targetFramework.Value);
            Assert.Equal("'$(TargetFramework)' == ''", targetFramework.Parent!.Attribute("Condition")!.Value);

            XDocument edgeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.proj"));
            XElement when = Assert.Single(
                edgeReplay.Descendants("When"),
                element => element.Attribute("Condition")!.Value.Contains("net8.0", StringComparison.Ordinal));
            XElement selectedReference = Assert.Single(when.Descendants("_ReplayProjectReference"));
            Assert.Equal("$(RepoRoot)src/Child.csproj", selectedReference.Attribute("Include")!.Value);
            Assert.Empty(edgeReplay.Descendants("Target"));
            XElement replayedReference = Assert.Single(
                edgeReplay.Descendants("ProjectReference"),
                element => element.Attribute("Update") is not null);
            Assert.Equal("$(RepoRoot)src/Child.csproj", replayedReference.Attribute("Update")!.Value);
            Assert.Equal("true", replayedReference.Element("SkipGetTargetFrameworkProperties")!.Value);
            Assert.Equal("TargetFramework=net8.0", replayedReference.Element("ProjectReferenceReplaySetTargetFramework")!.Value);
            Assert.Null(replayedReference.Element("SetTargetFramework"));
            Assert.Null(replayedReference.Element("UndefineProperties"));
            Assert.Single(edgeReplay.Descendants("_ProjectReferenceToRemove"), element => element.Attribute("Include") is not null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplaysSingleSelectedFrameworkForUnspecifiedEdge()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllLines(captureFile,
            [
                "src/Parent.csproj|net8.0|src/Child.csproj|",
                "src/Other.csproj|net8.0|src/Child.csproj|TargetFramework=net8.0",
            ]);

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            XDocument replay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.csproj"));
            XElement replayedReference = Assert.Single(
                replay.Descendants("ProjectReference"),
                element => element.Attribute("Update") is not null);
            Assert.Equal("TargetFramework=net8.0", replayedReference.Element("SetTargetFramework")!.Value);
            Assert.Null(replayedReference.Element("UndefineProperties"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReplaysSingleTargetFrameworkReferenceWithoutNegotiation()
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(captureFile, "src/Parent.csproj|net8.0|src/Child.csproj|");

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            XDocument edgeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.csproj"));
            XElement replayedReference = Assert.Single(
                edgeReplay.Descendants("ProjectReference"),
                element => element.Attribute("Update") is not null);
            Assert.Equal("%(ProjectReference.UndefineProperties);TargetFramework", replayedReference.Element("UndefineProperties")!.Value);
            Assert.Null(replayedReference.Element("SetTargetFramework"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("")]
    public void GeneratesAuthoritativeEmptyEdgeSet(string targetFramework)
    {
        string directory = CreateTemporaryDirectory();

        try
        {
            string captureFile = Path.Combine(directory, "capture.txt");
            string replayFile = Path.Combine(directory, "replay.targets");
            File.WriteAllText(captureFile, $"src/Parent.proj|{targetFramework}||");

            GenerateProjectReferenceReplay task = new()
            {
                BuildEngine = new TestBuildEngine(),
                RawSelectionFile = captureFile,
                OutputFile = replayFile,
            };

            Assert.True(task.Execute());

            XDocument edgeReplay = XDocument.Load(GetReplayFile(replayFile, "src/Parent.proj"));
            XElement when = Assert.Single(edgeReplay.Descendants("When"));
            Assert.Empty(when.Descendants("_ReplayProjectReference"));
            Assert.Equal("true", Assert.Single(when.Descendants("_ApplyProjectReferenceReplayAllowList")).Value);
            Assert.Single(edgeReplay.Descendants("ProjectReference"), element => element.Attribute("Remove") is not null);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("/rooted/Parent.proj|net8.0||")]
    [InlineData("../Parent.proj|net8.0||")]
    [InlineData("src/Parent.proj|net'8.0||")]
    [InlineData("src/Parent.proj|net'8.0|src/Child.csproj|TargetFramework=net8.0")]
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
