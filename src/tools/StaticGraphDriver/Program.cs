// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

return StaticGraphDriver.Run(args);

internal static partial class StaticGraphDriver
{
    public static int Run(string[] args)
    {
        ValidationRun? run = null;
        try
        {
            string repoRoot = FindRepoRoot();
            string target = args.FirstOrDefault() ??
                Environment.GetEnvironmentVariable("STATIC_GRAPH_TARGET") ??
                "clr";

            if (args.Length > 1)
            {
                throw new ValidationException("Expected at most one validation target.");
            }

            ValidationTarget validationTarget = ValidationTarget.Create(target);
            run = new ValidationRun(repoRoot, validationTarget);
            int exitCode = run.Execute();
            run.Dispose();
            return run.InterruptedExitCode ?? exitCode;
        }
        catch (ValidationInterruptedException ex)
        {
            return ex.ExitCode;
        }
        catch (ValidationException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            run?.Dispose();
        }
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(Directory.GetCurrentDirectory());
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Build.proj")) &&
                File.Exists(Path.Combine(directory.FullName, "dotnet.sh")))
            {
                return directory.FullName;
            }
        }

        throw new ValidationException(
            "Unable to find the runtime repository root. Run this tool from within a runtime checkout.");
    }
}

internal sealed partial class ValidationRun : IDisposable
{
    private readonly string _repoRoot;
    private readonly ValidationTarget _target;
    private readonly string _configuration;
    private readonly string _runtimeConfiguration;
    private readonly string _targetArchitecture;
    private readonly string _buildArchitecture;
    private readonly string _tasksConfiguration;
    private readonly string _validationToolRoot;
    private readonly string _outputDirectory;
    private readonly string _validationOutputDirectory;
    private readonly string _diffOutputDirectory;
    private readonly string _nodesOutputDirectory;
    private readonly string _targetsOutputDirectory;
    private readonly string _comparisonOutputDirectory;
    private readonly string _binlogOutputDirectory;
    private readonly string _diffOutput;
    private readonly string _dynamicNodesOutput;
    private readonly string _staticNodesOutput;
    private readonly string _comparisonOutput;
    private readonly string _workDirectory;
    private readonly string? _reuseDynamicBinlog;
    private readonly List<string> _commonProperties;
    private readonly List<string> _prerequisiteProperties = [];
    private readonly object _disposeLock = new();
    private readonly PosixSignalRegistration? _sigintRegistration;
    private readonly PosixSignalRegistration? _sigtermRegistration;
    private string? _prerequisiteArtifactsSnapshot;
    private bool _succeeded;
    private bool _disposed;
    private int _signalExitCode;

    public int? InterruptedExitCode =>
        Volatile.Read(ref _signalExitCode) is int exitCode and not 0 ? exitCode : null;

    public ValidationRun(string repoRoot, ValidationTarget target)
    {
        _repoRoot = repoRoot;
        _target = target;
        _configuration = GetEnvironmentValue("CONFIGURATION", "Debug");
        _runtimeConfiguration = GetEnvironmentValue("RUNTIME_CONFIGURATION", "Release");
        _targetArchitecture = GetEnvironmentValue("TARGET_ARCHITECTURE", "x64");
        _buildArchitecture = GetEnvironmentValue("BUILD_ARCHITECTURE", "x64");
        _tasksConfiguration = GetEnvironmentValue("TASKS_CONFIGURATION", "Debug");
        _validationToolRoot = GetEnvironmentValue(
            "MSBUILD_GRAPH_VALIDATION_ROOT",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "src", "msbuild-graph-validation"));

        _outputDirectory = Path.Combine(_repoRoot, "artifacts", "log", "static-graph-validation", _target.Name);
        _validationOutputDirectory = Path.Combine(_repoRoot, "static-graph-validation");
        _diffOutputDirectory = Path.Combine(_validationOutputDirectory, "diffs");
        _nodesOutputDirectory = Path.Combine(_validationOutputDirectory, "nodes");
        _targetsOutputDirectory = Path.Combine(_validationOutputDirectory, "targets", _target.Name);
        _comparisonOutputDirectory = Path.Combine(_validationOutputDirectory, "comparisons");
        _binlogOutputDirectory = Path.Combine(_validationOutputDirectory, "binlogs", _target.Name);
        _diffOutput = Path.Combine(_diffOutputDirectory, $"static-graph-{_target.Name}.diff");
        _dynamicNodesOutput = Path.Combine(_nodesOutputDirectory, $"{_target.Name}.dynamic.nodes.txt");
        _staticNodesOutput = Path.Combine(_nodesOutputDirectory, $"{_target.Name}.static.nodes.txt");
        _comparisonOutput = Path.Combine(_comparisonOutputDirectory, $"static-graph-{_target.Name}.comparison.txt");
        _workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"runtime-static-graph-validation.{Path.GetRandomFileName()}");

        string? reuseDynamicBinlog = Environment.GetEnvironmentVariable("REUSE_DYNAMIC_BINLOG");
        if (string.Equals(reuseDynamicBinlog, "true", StringComparison.OrdinalIgnoreCase))
        {
            reuseDynamicBinlog = Path.Combine(_binlogOutputDirectory, $"dynamic-{_target.Name}.binlog");
        }
        else if (!string.IsNullOrWhiteSpace(reuseDynamicBinlog) &&
                 !Path.IsPathFullyQualified(reuseDynamicBinlog))
        {
            reuseDynamicBinlog = Path.GetFullPath(reuseDynamicBinlog, _repoRoot);
        }

        if (!string.IsNullOrWhiteSpace(reuseDynamicBinlog) && !File.Exists(reuseDynamicBinlog))
        {
            throw new ValidationException($"Dynamic binlog to reuse does not exist: {reuseDynamicBinlog}");
        }

        _reuseDynamicBinlog = reuseDynamicBinlog;

        try
        {
            Directory.CreateDirectory(_workDirectory);
            if (_reuseDynamicBinlog is not null)
            {
                File.Copy(
                    _reuseDynamicBinlog,
                    Path.Combine(_workDirectory, $"dynamic-{_target.Name}.binlog"));
            }

            Directory.CreateDirectory(_diffOutputDirectory);
            Directory.CreateDirectory(_nodesOutputDirectory);
            Directory.CreateDirectory(_comparisonOutputDirectory);
            Directory.CreateDirectory(_binlogOutputDirectory);
        }
        catch
        {
            if (Directory.Exists(_workDirectory))
            {
                Directory.Delete(_workDirectory, recursive: true);
            }

            throw;
        }

        if (!OperatingSystem.IsWindows())
        {
            _sigintRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGINT,
                context => HandleSignal(context, 130));
            _sigtermRegistration = PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                context => HandleSignal(context, 143));
        }

        _commonProperties =
        [
            $"/p:Configuration={_configuration}",
            $"/p:RuntimeConfiguration={_runtimeConfiguration}",
            "/p:Restore=false",
            "/p:Build=true",
            "/p:Ninja=true",
            $"/p:TargetArchitecture={_targetArchitecture}",
            $"/p:BuildArchitecture={_buildArchitecture}",
            "/p:FeatureDynamicCodeCompiled=true",
        ];

        if (string.Equals(_target.EntryProject, "Build.proj", StringComparison.Ordinal))
        {
            _commonProperties.Insert(0, $"/p:Subset={_target.Name}");
        }

        if (string.Equals(_target.Name, "libs.sfx", StringComparison.Ordinal))
        {
            _commonProperties.Insert(0, "/p:ApiCompatValidateAssemblies=false");
        }

        if ((_target.Name is "host.pretest" or "host.tests") &&
            (_configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
             _configuration.Equals("Checked", StringComparison.OrdinalIgnoreCase)))
        {
            _commonProperties.Insert(0, "/p:FeatureInterpreter=true");
        }

        if (_target.Name == "host.tests" &&
            (_configuration.Equals("Debug", StringComparison.OrdinalIgnoreCase) ||
             _configuration.Equals("Checked", StringComparison.OrdinalIgnoreCase)))
        {
            _prerequisiteProperties.Add("/p:FeatureInterpreter=true");
        }
    }

    public int Execute()
    {
        try
        {
            return ExecuteCore();
        }
        catch (Exception) when (InterruptedExitCode is int exitCode)
        {
            throw new ValidationInterruptedException(exitCode);
        }
    }

    private int ExecuteCore()
    {
        Directory.SetCurrentDirectory(_repoRoot);
        ThrowIfInterrupted();

        Log("Removing artifacts for a clean validation run");
        CleanBuildArtifacts();

        Log($"Restoring {_target.RestoreLabel}");
        RunTimed("restore dynamic", BuildRestoreArguments("restore-dynamic"));

        BuildPrerequisites("dynamic");

        if (_target.HasPrerequisites)
        {
            Log("Snapshotting prerequisite artifacts for the isolated static graph build");
            RunTimed("snapshot prerequisite artifacts", SnapshotPrerequisiteArtifacts);
        }

        List<string> staticGraphProperties = [];
        string? captureFile = null;
        string? replayFile = null;
        string? replayGeneratorTasks = null;

        if (_target.UseProjectReferenceReplay)
        {
            captureFile = Path.Combine(_workDirectory, "project-references.raw.txt");
            replayFile = Path.Combine(_targetsOutputDirectory, "project-references.replay.targets");
            Directory.CreateDirectory(_targetsOutputDirectory);
            string staticGraphTasks = Path.Combine(
                _repoRoot,
                "artifacts",
                "bin",
                "StaticGraphTasks",
                _tasksConfiguration,
                "net11.0",
                "StaticGraphTasks.dll");
            replayGeneratorTasks = Path.Combine(_workDirectory, "StaticGraphTasks.dll");

            Log("Building project reference capture and replay tasks");
            RunTimed(
                "build replay tasks",
                "./dotnet.sh",
                [
                    "build",
                    "src/tasks/StaticGraphTasks/StaticGraphTasks.csproj",
                    "--configuration",
                    _tasksConfiguration,
                    "--nologo",
                    "--verbosity:minimal",
                ]);

            Log("Capturing the negotiated dynamic project graph");
            RunTimed(
                "capture dynamic graph",
                "./dotnet.sh",
                BuildMsBuildArguments(
                    _target.EntryProject,
                    [
                        "/t:CaptureProjectReferencesRecursive",
                        "/nr:false",
                        .. _target.CaptureParallelism,
                        $"/bl:{Path.Combine(_workDirectory, $"capture-{_target.Name}.binlog")}",
                        MsBuildLogArgument("capture-dynamic-graph"),
                        "/p:CaptureProjectReferences=true",
                        $"/p:CaptureProjectReferencesFile={captureFile}",
                        $"/p:StaticGraphTasksAssemblyPath={staticGraphTasks}",
                        .. _commonProperties,
                    ]));

            File.Copy(staticGraphTasks, replayGeneratorTasks, overwrite: true);
            staticGraphProperties.Add("/p:UseProjectReferenceReplay=true");
            staticGraphProperties.Add($"/p:ProjectReferenceReplayTargets={replayFile}");
        }

        string dynamicBinlog = Path.Combine(_workDirectory, $"dynamic-{_target.Name}.binlog");
        if (_reuseDynamicBinlog is not null)
        {
            Log($"Reusing dynamic non-graph {_target.Name} build binlog from {_reuseDynamicBinlog}");
        }
        else
        {
            Log($"Capturing dynamic non-graph {_target.Name} build");
            RunTimed(
                "dynamic build",
                "./dotnet.sh",
                BuildMsBuildArguments(
                    _target.EntryProject,
                    [
                        "/nr:false",
                        .. _target.DynamicBuildParallelism,
                        $"/bl:{dynamicBinlog}",
                        MsBuildLogArgument("dynamic-build"),
                        .. _commonProperties,
                    ]));
        }

        Log("Removing artifacts before the isolated static graph build");
        CleanBuildArtifacts();

        if (_target.UseProjectReferenceReplay)
        {
            Log("Generating project reference replay targets");
            RunTimed(
                "generate project reference replay",
                "./dotnet.sh",
                BuildMsBuildArguments(
                    "eng/projectReferenceReplay.targets",
                    [
                        "/t:GenerateProjectReferenceReplay",
                        "/nr:false",
                        MsBuildLogArgument("generate-project-reference-replay"),
                        $"/p:CaptureProjectReferencesFile={captureFile}",
                        $"/p:ProjectReferenceReplayTargets={replayFile}",
                        $"/p:StaticGraphTasksAssemblyPath={replayGeneratorTasks}",
                    ]));
            File.Delete(replayGeneratorTasks!);
        }

        if (_prerequisiteArtifactsSnapshot is not null)
        {
            Log("Restoring prerequisite artifacts for the isolated static graph build");
            RunTimed("restore prerequisite artifacts", RestorePrerequisiteArtifacts);
        }
        else
        {
            Log($"Restoring {_target.RestoreLabel} for the isolated static graph build");
            RunTimed("restore static", BuildRestoreArguments("restore-static"));
            BuildPrerequisites("static");
        }

        Log($"Running isolated static graph {_target.Name} build");
        string staticBinlog = Path.Combine(_workDirectory, $"static-{_target.Name}.binlog");
        RunTimed(
            "static graph build",
            "./dotnet.sh",
            BuildMsBuildArguments(
                _target.EntryProject,
                [
                    "/graphBuild",
                    "/isolateProjects",
                    "/nr:false",
                    .. _target.StaticGraphParallelism,
                    $"/bl:{staticBinlog}",
                    MsBuildLogArgument("static-build"),
                    .. staticGraphProperties,
                    .. _commonProperties,
                ]));

        ValidateStaticGraphSize();
        CompareGraphs(dynamicBinlog, staticGraphProperties);

        ThrowIfInterrupted();
        _succeeded = true;
        return 0;
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                Cleanup();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to collect validation artifacts: {ex.Message}");
                if (_succeeded)
                {
                    throw;
                }
            }
        }
    }

    private void HandleSignal(PosixSignalContext context, int exitCode)
    {
        context.Cancel = true;
        Interlocked.CompareExchange(ref _signalExitCode, exitCode, comparand: 0);
        try
        {
            ProcessRunner.TerminateCurrentProcessTree();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to terminate the active validation command: {ex.Message}");
        }
    }

    private void CompareGraphs(string dynamicBinlog, List<string> staticGraphProperties)
    {
        Log("Comparing dynamic and static graph binlogs");
        string dynamicJson = Path.Combine(_workDirectory, "dynamic.json");
        string staticJson = Path.Combine(_workDirectory, "static.json");
        RunGraphValidationTool(
            [
                "dynamic",
                dynamicBinlog,
                "--repo-root",
                _repoRoot,
                "-o",
                dynamicJson,
            ]);

        List<string> staticCaptureArguments =
        [
            "static",
            _target.EntryProject,
            "--repo-root",
            _repoRoot,
            "-t",
            "Build",
        ];
        foreach (string property in staticGraphProperties.Concat(_commonProperties))
        {
            staticCaptureArguments.Add("-p");
            staticCaptureArguments.Add(property["/p:".Length..]);
        }
        staticCaptureArguments.Add("-o");
        staticCaptureArguments.Add(staticJson);
        RunGraphValidationTool(staticCaptureArguments);

        string normalizedDynamic = Path.Combine(_workDirectory, "normalized", "dynamic");
        string normalizedStatic = Path.Combine(_workDirectory, "normalized", "static");
        RunGraphValidationTool(["normalize", dynamicJson, "--output-dir", normalizedDynamic]);
        RunGraphValidationTool(["normalize", staticJson, "--output-dir", normalizedStatic]);

        foreach (string fileName in new[] { "nodes.txt", "edges.txt" })
        {
            RemoveRepoRoot(Path.Combine(normalizedDynamic, fileName));
            RemoveRepoRoot(Path.Combine(normalizedStatic, fileName));
        }

        File.Copy(Path.Combine(normalizedDynamic, "nodes.txt"), _dynamicNodesOutput, overwrite: true);
        File.Copy(Path.Combine(normalizedStatic, "nodes.txt"), _staticNodesOutput, overwrite: true);
        WriteFullContextDiff(
            _dynamicNodesOutput,
            _staticNodesOutput,
            "dynamic/nodes.txt",
            "static/nodes.txt",
            _diffOutput);
        File.Copy(
            _diffOutput,
            Path.Combine(_workDirectory, $"static-graph-{_target.Name}.diff"),
            overwrite: true);

        string comparison = Path.Combine(_workDirectory, "comparison.txt");
        int comparisonExitCode = RunGraphValidationTool(
            [
                "compare",
                "--left",
                dynamicJson,
                "--right",
                staticJson,
                "--repo-root",
                _repoRoot,
                "-o",
                comparison,
            ],
            allowedExitCodes: new HashSet<int> { 0, 2 });
        File.Copy(comparison, _comparisonOutput, overwrite: true);

        Console.WriteLine();
        Console.WriteLine(
            comparisonExitCode == 0
                ? "Static graph binlog comparison passed"
                : "Static graph binlog comparison completed with differences");
        Console.WriteLine($"Artifacts copied to {_outputDirectory}");
        Console.WriteLine($"Binlogs copied to {_binlogOutputDirectory}");
        Console.WriteLine($"Diff written to {_diffOutput}");
        Console.WriteLine($"Node files written to {_dynamicNodesOutput} and {_staticNodesOutput}");
        Console.WriteLine($"Comparison report written to {_comparisonOutput}");
    }

    private void BuildPrerequisites(string phase)
    {
        ThrowIfInterrupted();
        if (_target.PrerequisiteSubsets.Count > 0)
        {
            Log($"Building {string.Join(' ', _target.PrerequisiteSubsets)} artifact prerequisites for the {phase} build");
            RunTimed(
                $"build {phase} subset prerequisites",
                "./build.sh",
                [
                    .. _target.PrerequisiteSubsets,
                    "-a",
                    _targetArchitecture,
                    "-c",
                    _configuration,
                    "-lc",
                    _configuration,
                    "-rc",
                    _runtimeConfiguration,
                    $"/p:BuildArchitecture={_buildArchitecture}",
                    .. _prerequisiteProperties,
                ]);
        }

        foreach (string prerequisiteProject in _target.PrerequisiteProjects)
        {
            ThrowIfInterrupted();
            string prerequisiteName = Path.GetFileName(prerequisiteProject);
            Log($"Restoring {prerequisiteProject} for the {phase} build");
            RunTimed(
                $"restore {phase} prerequisite {prerequisiteName}",
                "./dotnet.sh",
                BuildMsBuildArguments(
                    prerequisiteProject,
                    [
                        "/t:Restore",
                        "/nr:false",
                        MsBuildLogArgument($"{phase}-{prerequisiteName}-prerequisite-restore"),
                        $"/p:Configuration={_configuration}",
                        $"/p:TargetArchitecture={_targetArchitecture}",
                        $"/p:BuildArchitecture={_buildArchitecture}",
                    ]));

            Log($"Building {prerequisiteProject} for the {phase} build");
            RunTimed(
                $"build {phase} prerequisite {prerequisiteName}",
                "./dotnet.sh",
                BuildMsBuildArguments(
                    prerequisiteProject,
                    [
                        "/nr:false",
                        .. _target.DynamicBuildParallelism,
                        MsBuildLogArgument($"{phase}-{prerequisiteName}-prerequisite-build"),
                        .. _commonProperties,
                    ]));
        }
    }

    private void ValidateStaticGraphSize()
    {
        if (_target.MaxStaticGraphNodes == 0 && _target.MaxStaticGraphEdges == 0)
        {
            return;
        }

        string log = File.ReadAllText(Path.Combine(_workDirectory, "static-build.log"));
        Match match = StaticGraphSizeRegex().Match(log);
        if (!match.Success)
        {
            throw new ValidationException("Unable to determine the static graph size.");
        }

        int nodes = int.Parse(match.Groups["nodes"].Value, CultureInfo.InvariantCulture);
        int edges = int.Parse(match.Groups["edges"].Value, CultureInfo.InvariantCulture);
        if (_target.MaxStaticGraphNodes > 0 && nodes > _target.MaxStaticGraphNodes)
        {
            throw new ValidationException(
                $"Static graph contains {nodes} nodes, exceeding the limit of {_target.MaxStaticGraphNodes}.");
        }

        if (_target.MaxStaticGraphEdges > 0 && edges > _target.MaxStaticGraphEdges)
        {
            throw new ValidationException(
                $"Static graph contains {edges} edges, exceeding the limit of {_target.MaxStaticGraphEdges}.");
        }

        Console.WriteLine(
            $"Static graph size: {nodes} nodes / {edges} edges " +
            $"(limits: {_target.MaxStaticGraphNodes} / {_target.MaxStaticGraphEdges})");
    }

    private List<string> BuildRestoreArguments(string logName)
    {
        List<string> arguments = [.. _target.RestoreCommand];
        arguments.Add(MsBuildLogArgument(logName));
        return arguments;
    }

    private static List<string> BuildMsBuildArguments(string project, IEnumerable<string> arguments) =>
        ["msbuild", project, .. arguments];

    private string MsBuildLogArgument(string name) =>
        $"/flp:LogFile={Path.Combine(_workDirectory, $"{name}.log")};Verbosity=normal";

    private void SnapshotPrerequisiteArtifacts()
    {
        _prerequisiteArtifactsSnapshot = Path.Combine(
            _repoRoot,
            $".artifacts-prerequisite-snapshot.{Path.GetRandomFileName()}");
        Directory.CreateDirectory(_prerequisiteArtifactsSnapshot);
        CopyBuildArtifacts(
            Path.Combine(_repoRoot, "artifacts"),
            _prerequisiteArtifactsSnapshot);
    }

    private void RestorePrerequisiteArtifacts()
    {
        CleanBuildArtifacts();
        MoveBuildArtifacts(
            _prerequisiteArtifactsSnapshot!,
            Path.Combine(_repoRoot, "artifacts"));
        Directory.Delete(_prerequisiteArtifactsSnapshot!, recursive: true);
        _prerequisiteArtifactsSnapshot = null;
    }

    private void CleanBuildArtifacts()
    {
        ThrowIfInterrupted();
        string artifacts = Path.Combine(_repoRoot, "artifacts");
        if (!Directory.Exists(artifacts))
        {
            return;
        }

        DeleteChildrenExcept(artifacts, "log");
        string logDirectory = Path.Combine(artifacts, "log");
        if (Directory.Exists(logDirectory))
        {
            DeleteChildrenExcept(logDirectory, "static-graph-validation");
        }
    }

    private void DeleteChildrenExcept(string directory, string exceptName)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            ThrowIfInterrupted();
            if (string.Equals(Path.GetFileName(path), exceptName, StringComparison.Ordinal))
            {
                continue;
            }

            DeletePath(path);
        }
    }

    private void CopyBuildArtifacts(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string source in Directory.EnumerateFileSystemEntries(sourceRoot))
        {
            ThrowIfInterrupted();
            if (string.Equals(Path.GetFileName(source), "log", StringComparison.Ordinal))
            {
                continue;
            }

            CopyPreservingAttributes(source, destinationRoot);
        }

        string sourceLog = Path.Combine(sourceRoot, "log");
        if (!Directory.Exists(sourceLog))
        {
            return;
        }

        string destinationLog = Path.Combine(destinationRoot, "log");
        Directory.CreateDirectory(destinationLog);
        foreach (string source in Directory.EnumerateFileSystemEntries(sourceLog))
        {
            ThrowIfInterrupted();
            if (string.Equals(
                    Path.GetFileName(source),
                    "static-graph-validation",
                    StringComparison.Ordinal))
            {
                continue;
            }

            CopyPreservingAttributes(source, destinationLog);
        }
    }

    private void MoveBuildArtifacts(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (string source in Directory.EnumerateFileSystemEntries(sourceRoot).ToArray())
        {
            ThrowIfInterrupted();
            if (string.Equals(Path.GetFileName(source), "log", StringComparison.Ordinal))
            {
                continue;
            }

            MovePath(source, Path.Combine(destinationRoot, Path.GetFileName(source)));
        }

        string sourceLog = Path.Combine(sourceRoot, "log");
        if (!Directory.Exists(sourceLog))
        {
            return;
        }

        string destinationLog = Path.Combine(destinationRoot, "log");
        Directory.CreateDirectory(destinationLog);
        foreach (string source in Directory.EnumerateFileSystemEntries(sourceLog).ToArray())
        {
            ThrowIfInterrupted();
            MovePath(source, Path.Combine(destinationLog, Path.GetFileName(source)));
        }
    }

    private static void CopyPreservingAttributes(string source, string destinationDirectory)
    {
        ProcessRunner.Run(
            "cp",
            ["-a", source, destinationDirectory],
            Directory.GetCurrentDirectory());
    }

    private static void MovePath(string source, string destination)
    {
        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private int RunGraphValidationTool(
        IReadOnlyList<string> arguments,
        IReadOnlySet<int>? allowedExitCodes = null)
    {
        string toolDll = Path.Combine(
            _validationToolRoot,
            "artifacts",
            "bin",
            "StaticGraphValidation",
            "debug",
            "StaticGraphValidation.dll");
        if (!File.Exists(toolDll))
        {
            ProcessRunner.Run(
                "dotnet",
                [
                    "build",
                    Path.Combine(_validationToolRoot, "msbuild-graph-validation.slnx"),
                    "-v:minimal",
                ],
                _repoRoot);
        }

        return ProcessRunner.Run(
            "./dotnet.sh",
            [toolDll, .. arguments],
            _repoRoot,
            allowedExitCodes,
            new Dictionary<string, string?> { ["DOTNET_ROLL_FORWARD"] = "Major" });
    }

    private void RunTimed(string label, List<string> command) =>
        RunTimed(label, command[0], command.Skip(1).ToArray());

    private void RunTimed(string label, string fileName, IReadOnlyList<string> arguments)
    {
        RunTimed(
            label,
            () => ProcessRunner.Run(fileName, arguments, _repoRoot));
    }

    private void RunTimed(string label, Action action)
    {
        ThrowIfInterrupted();
        Stopwatch stopwatch = Stopwatch.StartNew();
        action();
        ThrowIfInterrupted();
        stopwatch.Stop();

        string timing = $"{label}: {stopwatch.Elapsed:hh\\:mm\\:ss}";
        Console.WriteLine(timing);
        File.AppendAllText(
            Path.Combine(_workDirectory, "timings.txt"),
            timing + Environment.NewLine);
    }

    private static void WriteFullContextDiff(
        string left,
        string right,
        string leftLabel,
        string rightLabel,
        string output)
    {
        ProcessRunner.RunToFile(
            "diff",
            ["-u", "-U1000000", "--label", leftLabel, "--label", rightLabel, left, right],
            Directory.GetCurrentDirectory(),
            output,
            allowedExitCodes: new HashSet<int> { 0, 1 });

        if (new FileInfo(output).Length > 0)
        {
            return;
        }

        string[] lines = File.ReadAllLines(left);
        using StreamWriter writer = new(output, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.WriteLine($"--- {leftLabel}");
        writer.WriteLine($"+++ {rightLabel}");
        writer.WriteLine($"@@ -1,{lines.Length} +1,{lines.Length} @@");
        foreach (string line in lines)
        {
            writer.WriteLine($" {line}");
        }
    }

    private void RemoveRepoRoot(string path)
    {
        string contents = File.ReadAllText(path);
        File.WriteAllText(path, contents.Replace(_repoRoot + "/", "", StringComparison.Ordinal));
    }

    private void Cleanup()
    {
        if (_prerequisiteArtifactsSnapshot is not null &&
            Directory.Exists(_prerequisiteArtifactsSnapshot))
        {
            if (_succeeded)
            {
                Directory.Delete(_prerequisiteArtifactsSnapshot, recursive: true);
            }
            else
            {
                Console.Error.WriteLine(
                    $"Prerequisite artifact snapshot retained after failure: {_prerequisiteArtifactsSnapshot}");
            }
        }

        Directory.CreateDirectory(_outputDirectory);
        Directory.CreateDirectory(_binlogOutputDirectory);
        if (_succeeded)
        {
            foreach (string binlog in Directory.EnumerateFiles(_binlogOutputDirectory, "*.binlog"))
            {
                File.Delete(binlog);
            }
        }

        foreach (string binlog in Directory.EnumerateFiles(_workDirectory, "*.binlog"))
        {
            File.Move(
                binlog,
                Path.Combine(_binlogOutputDirectory, Path.GetFileName(binlog)),
                overwrite: true);
        }

        CopyDirectoryContents(_workDirectory, _outputDirectory);
        Directory.Delete(_workDirectory, recursive: true);
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destinationFile = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(file, destinationFile, overwrite: true);
        }
    }

    private static void DeletePath(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
        else
        {
            File.Delete(path);
        }
    }

    private static string GetEnvironmentValue(string name, string defaultValue) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;

    private void ThrowIfInterrupted()
    {
        if (InterruptedExitCode is int exitCode)
        {
            throw new ValidationInterruptedException(exitCode);
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine();
        Console.WriteLine($"==> {message}");
    }

    [GeneratedRegex(@"Static graph loaded.*: (?<nodes>[0-9]+) nodes, (?<edges>[0-9]+) edges")]
    private static partial Regex StaticGraphSizeRegex();
}

internal sealed record ValidationTarget
{
    public required string Name { get; init; }
    public required string EntryProject { get; init; }
    public required IReadOnlyList<string> RestoreCommand { get; init; }
    public string RestoreLabel => Name;
    public bool UseProjectReferenceReplay { get; init; }
    public IReadOnlyList<string> DynamicBuildParallelism { get; init; } = [];
    public IReadOnlyList<string> CaptureParallelism { get; init; } = [];
    public IReadOnlyList<string> StaticGraphParallelism { get; init; } = [];
    public int MaxStaticGraphNodes { get; init; }
    public int MaxStaticGraphEdges { get; init; }
    public IReadOnlyList<string> PrerequisiteSubsets { get; init; } = [];
    public IReadOnlyList<string> PrerequisiteProjects { get; init; } = [];
    public bool HasPrerequisites => PrerequisiteSubsets.Count > 0 || PrerequisiteProjects.Count > 0;

    public static ValidationTarget Create(string name) =>
        name switch
        {
            "clr" => BuildTarget(name),
            "libs.native" => new ValidationTarget
            {
                Name = name,
                EntryProject = "src/native/libs/build-native.proj",
                RestoreCommand =
                [
                    "./dotnet.sh",
                    "msbuild",
                    "src/native/libs/build-native.proj",
                    "/t:Restore",
                    "/p:Configuration={configuration}",
                    "/p:TargetArchitecture={targetArchitecture}",
                    "/p:BuildArchitecture={buildArchitecture}",
                ],
            },
            "libs.sfx" => BuildTarget(
                name,
                replay: true,
                dynamicParallelism: ["/m:1"],
                captureParallelism: ["/m:2"],
                staticParallelism: ["/m:2"],
                maxNodes: 1000,
                maxEdges: 50000),
            "libs.pretest" => BuildTarget(
                name,
                prerequisites: ["host.native+clr.runtime+clr.corelib+libs.sfx"],
                replay: true,
                dynamicParallelism: ["/m:1"],
                captureParallelism: ["/m:2"],
                staticParallelism: ["/m:2"],
                maxNodes: 1000,
                maxEdges: 50000),
            "libs.tests" => BuildTarget(
                name,
                prerequisites: ["host.native+clr.runtime+clr.corelib+libs.sfx+libs.pretest"],
                replay: true,
                dynamicParallelism: ["/m:1"],
                captureParallelism: ["/m:2"],
                staticParallelism: ["/m:2"],
                maxNodes: 10000,
                maxEdges: 500000),
            "host.native" => BuildTarget(
                name,
                dynamicParallelism: ["/m:1"],
                staticParallelism: ["/m:2"],
                maxNodes: 100,
                maxEdges: 1000),
            "host.tools" => BuildTarget(
                name,
                dynamicParallelism: ["/m:1"],
                staticParallelism: ["/m:2"],
                maxNodes: 100,
                maxEdges: 1000),
            "host.pkg" => BuildTarget(
                name,
                prerequisites: ["clr.runtime+host.native"],
                dynamicParallelism: ["/m:1"],
                staticParallelism: ["/m:2"],
                maxNodes: 100,
                maxEdges: 1000),
            "host.pretest" => BuildTarget(
                name,
                prerequisites:
                [
                    "host.native+clr.runtime+clr.corelib+clr.tools+libs.native+libs.sfx+libs.pretest",
                ],
                dynamicParallelism: ["/m:1"],
                staticParallelism: ["/m:2"],
                maxNodes: 1000,
                maxEdges: 50000),
            "host.tests" => BuildTarget(
                name,
                prerequisites:
                [
                    "host.native+clr.runtime+clr.corelib+clr.tools+libs.native+libs.sfx+libs.pretest+host.pretest",
                ],
                replay: true,
                dynamicParallelism: ["/m:1"],
                captureParallelism: ["/m:2"],
                staticParallelism: ["/m:2"],
                maxNodes: 1000,
                maxEdges: 50000),
            "libs.oob" => new ValidationTarget
            {
                Name = name,
                EntryProject = "src/libraries/oob.proj",
                PrerequisiteProjects =
                [
                    "src/libraries/sfx-gen.proj",
                    "src/libraries/sfx-src.proj",
                    "src/libraries/sfx-finish.proj",
                    "src/native/libs/build-native.proj",
                ],
                RestoreCommand =
                [
                    "./dotnet.sh",
                    "msbuild",
                    "src/libraries/oob.proj",
                    "/t:Restore",
                    "/p:Configuration={configuration}",
                    "/p:TargetArchitecture={targetArchitecture}",
                    "/p:BuildArchitecture={buildArchitecture}",
                ],
                UseProjectReferenceReplay = true,
                DynamicBuildParallelism = ["/m:1"],
                CaptureParallelism = ["/m:2"],
                StaticGraphParallelism = ["/m:2"],
                MaxStaticGraphNodes = 1000,
                MaxStaticGraphEdges = 50000,
            },
            _ => throw new ValidationException(
                $"Unsupported static graph validation target '{name}'. Supported targets: " +
                "clr, libs.native, libs.sfx, libs.pretest, libs.tests, libs.oob, " +
                "host.native, host.tools, host.pkg, host.pretest, host.tests"),
        };

    private static ValidationTarget BuildTarget(
        string name,
        IReadOnlyList<string>? prerequisites = null,
        bool replay = false,
        IReadOnlyList<string>? dynamicParallelism = null,
        IReadOnlyList<string>? captureParallelism = null,
        IReadOnlyList<string>? staticParallelism = null,
        int maxNodes = 0,
        int maxEdges = 0) =>
        new()
        {
            Name = name,
            EntryProject = "Build.proj",
            RestoreCommand =
            [
                "./build.sh",
                name,
                "--restore",
                "--runtimeConfiguration",
                "{runtimeConfiguration}",
            ],
            PrerequisiteSubsets = prerequisites ?? [],
            UseProjectReferenceReplay = replay,
            DynamicBuildParallelism = dynamicParallelism ?? [],
            CaptureParallelism = captureParallelism ?? [],
            StaticGraphParallelism = staticParallelism ?? [],
            MaxStaticGraphNodes = maxNodes,
            MaxStaticGraphEdges = maxEdges,
        };
}

internal static class ProcessRunner
{
    private static readonly object s_processLock = new();
    private static Process? s_currentProcess;

    public static void TerminateCurrentProcessTree()
    {
        Process? process;
        lock (s_processLock)
        {
            process = s_currentProcess;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static int Run(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlySet<int>? allowedExitCodes = null,
        IReadOnlyDictionary<string, string?>? environment = null) =>
        RunCore(
            fileName,
            arguments,
            workingDirectory,
            allowedExitCodes,
            environment,
            standardOutputPath: null);

    public static int RunToFile(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string standardOutputPath,
        IReadOnlySet<int>? allowedExitCodes = null) =>
        RunCore(
            fileName,
            arguments,
            workingDirectory,
            allowedExitCodes,
            environment: null,
            standardOutputPath);

    private static int RunCore(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlySet<int>? allowedExitCodes,
        IReadOnlyDictionary<string, string?>? environment,
        string? standardOutputPath)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = standardOutputPath is not null,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(ExpandPlaceholders(argument));
        }

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        using Process process = Process.Start(startInfo) ??
            throw new ValidationException($"Failed to start '{fileName}'.");
        lock (s_processLock)
        {
            s_currentProcess = process;
        }

        try
        {
            if (standardOutputPath is not null)
            {
                using StreamWriter output = new(
                    standardOutputPath,
                    append: false,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                process.StandardOutput.BaseStream.CopyTo(output.BaseStream);
            }
            process.WaitForExit();

            allowedExitCodes ??= new HashSet<int> { 0 };
            if (!allowedExitCodes.Contains(process.ExitCode))
            {
                throw new ValidationException(
                    $"Command '{fileName}' exited with code {process.ExitCode}.");
            }

            return process.ExitCode;
        }
        finally
        {
            lock (s_processLock)
            {
                if (ReferenceEquals(s_currentProcess, process))
                {
                    s_currentProcess = null;
                }
            }
        }
    }

    private static string ExpandPlaceholders(string argument)
    {
        if (!argument.Contains('{', StringComparison.Ordinal))
        {
            return argument;
        }

        return argument
            .Replace(
                "{configuration}",
                GetEnvironmentValue("CONFIGURATION", "Debug"),
                StringComparison.Ordinal)
            .Replace(
                "{runtimeConfiguration}",
                GetEnvironmentValue("RUNTIME_CONFIGURATION", "Release"),
                StringComparison.Ordinal)
            .Replace(
                "{targetArchitecture}",
                GetEnvironmentValue("TARGET_ARCHITECTURE", "x64"),
                StringComparison.Ordinal)
            .Replace(
                "{buildArchitecture}",
                GetEnvironmentValue("BUILD_ARCHITECTURE", "x64"),
                StringComparison.Ordinal);
    }

    private static string GetEnvironmentValue(string name, string defaultValue) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : defaultValue;
}

internal sealed class ValidationException(string message) : Exception(message);

internal sealed class ValidationInterruptedException(int exitCode) : Exception
{
    public int ExitCode { get; } = exitCode;
}
