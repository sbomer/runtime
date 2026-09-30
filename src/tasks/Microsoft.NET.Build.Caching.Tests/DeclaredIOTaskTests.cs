// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.NET.Build.Caching.Tests;

[PlatformSpecific(TestPlatforms.Windows | TestPlatforms.Linux)]
public sealed class DeclaredIOTaskTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), nameof(DeclaredIOTaskTests), Guid.NewGuid().ToString("N"));

    private DeclaredIOTestTask Create(bool enabled = true)
    {
        Directory.CreateDirectory(_root);
        string input = Path.Combine(_root, "input");
        if (!File.Exists(input))
        {
            File.WriteAllText(input, "input");
        }

        return new DeclaredIOTestTask
        {
            BuildEngine = new TestEngine(),
            CacheDirectory = Path.Combine(_root, "cache"),
            CacheEnabled = enabled,
            Source = input,
            Destination = Path.Combine(_root, "output"),
            Value = 1.25f,
            Factor = 2
        };
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisabledCachingExecutesWithoutOpeningStore(bool missingDirectoryParameter)
    {
        DeclaredIOTestTask task = Create(enabled: false);
        string cache = task.CacheDirectory;
        if (missingDirectoryParameter)
        {
            task.CacheDirectory = "";
        }

        Assert.Equal(!missingDirectoryParameter, task.Execute());
        Assert.Equal(missingDirectoryParameter ? 0 : 1, task.Executions);
        Assert.False(Directory.Exists(cache));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HitReplaysFilesValuesAndMetadata(bool readOnly)
    {
        DeclaredIOTestTask first = Create();
        first.MakeReadOnly = readOnly;
        Assert.True(first.Execute());
        Assert.Equal(1, first.Executions);
        byte[] contents = File.ReadAllBytes(first.Destination);
        File.SetAttributes(first.Destination, FileAttributes.Normal);
        File.SetLastWriteTimeUtc(first.Destination, new DateTime(2000, 1, 1));
        if (readOnly)
        {
            File.SetAttributes(first.Destination, FileAttributes.ReadOnly);
        }
        DateTime before = DateTime.UtcNow.AddSeconds(-2);
        DeclaredIOTestTask second = Create();
        second.MakeReadOnly = readOnly;
        IBuildEngine engine = second.BuildEngine;
        Assert.True(second.Execute());
        Assert.Same(engine, second.BuildEngine);
        Assert.Equal(0, second.Executions);
        Assert.Equal(contents, File.ReadAllBytes(second.Destination));
        Assert.Equal(2.5f, second.Result);
        Assert.Equal(new string?[] { "", null, "literal%3B;value" }, second.Values);
        Assert.NotNull(second.Item);
        Assert.Equal("item%3B;name", second.Item.ItemSpec);
        Assert.Equal("value%3B;literal", second.Item.GetMetadata("Literal"));
        Assert.Equal("", second.Item.GetMetadata("OriginalItemSpec"));
        Assert.Equal(readOnly, (File.GetAttributes(second.Destination) & FileAttributes.ReadOnly) != 0);
        Assert.True(File.GetLastWriteTimeUtc(second.Destination) >= before);
    }

    [Theory]
    [InlineData("input")]
    [InlineData("value")]
    [InlineData("destination")]
    [InlineData("negative-zero")]
    public void FingerprintChangesForceExecution(string change)
    {
        DeclaredIOTestTask first = Create();
        first.Value = 0;
        Assert.True(first.Execute());
        DeclaredIOTestTask second = Create();
        second.Value = 0;
        switch (change)
        {
            case "input": File.WriteAllText(second.Source, "changed"); break;
            case "value": second.Factor = 3; break;
            case "destination": second.Destination += ".different"; break;
            case "negative-zero": second.Value = BitConverter.Int32BitsToSingle(int.MinValue); break;
        }

        Assert.True(second.Execute());
        Assert.Equal(1, second.Executions);
        Assert.Equal(2, Directory.GetFiles(first.CacheDirectory, "*.entry", SearchOption.AllDirectories).Length);
    }

    [ConditionalFact(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
    public void LoadedModuleIdentityDistinguishesTaskImplementations()
    {
        Type first = CreateTaskType();
        Type second = CreateTaskType();
        Assert.Equal(first.FullName, second.FullName);
        Assert.Equal(first.Assembly.FullName, second.Assembly.FullName);
        Assert.NotEqual(first.Module.ModuleVersionId, second.Module.ModuleVersionId);

        foreach ((Type type, int executions) in new[] { (first, 1), (first, 0), (second, 1), (second, 0) })
        {
            var engine = new TestEngine();
            var task = (DeclaredIOTask)Activator.CreateInstance(type)!;
            task.BuildEngine = engine;
            task.CacheDirectory = Path.Combine(_root, "cache");
            task.CacheEnabled = true;
            Assert.True(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
            Assert.Equal(executions, (int)type.GetField(nameof(DeclaredIOTestTask.Executions))!.GetValue(task)!);
        }

        Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "cache"), "*.entry", SearchOption.AllDirectories).Length);

        static Type CreateTaskType()
        {
            AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("TaskIdentity"), AssemblyBuilderAccess.RunAndCollect);
            TypeBuilder type = assembly.DefineDynamicModule("TaskIdentity").DefineType("TaskIdentity.Task", TypeAttributes.Public, typeof(DeclaredIOTask));
            type.DefineDefaultConstructor(MethodAttributes.Public);
            FieldBuilder executions = type.DefineField(nameof(DeclaredIOTestTask.Executions), typeof(int), FieldAttributes.Public);
            MethodBuilder describe = type.DefineMethod("DescribeOperation", MethodAttributes.Family | MethodAttributes.Virtual, typeof(void), new[] { typeof(TaskDeclaration) });
            describe.GetILGenerator().Emit(OpCodes.Ret);
            MethodBuilder execute = type.DefineMethod("ExecuteCore", MethodAttributes.Family | MethodAttributes.Virtual, typeof(bool), new[] { typeof(CancellationToken) });
            ILGenerator il = execute.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldfld, executions);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Stfld, executions);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            return type.CreateType()!;
        }
    }

    [Theory]
    [PlatformSpecific(TestPlatforms.Linux)]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedModuleIdentityDoesNotReadReplacedFiles(bool replaceBaseLibrary)
    {
        DeclaredIOTestTask paths = Create();
        string taskPath = Path.Combine(_root, "tasks.dll");
        string basePath = Path.Combine(_root, "base.dll");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Microsoft.NET.Build.Caching.Tests.dll"), taskPath);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Microsoft.NET.Build.Caching.dll"), basePath);
        var context = new TaskLoadContext(basePath);
        try
        {
            Type type = context.LoadFromAssemblyPath(taskPath).GetType(typeof(DeclaredIOTestTask).FullName!)!;
            Assert.Equal(basePath, type.BaseType!.Assembly.Location);
            Assert.Equal(1, Execute());

            string replacedPath = replaceBaseLibrary ? basePath : taskPath;
            File.Copy(typeof(IBuildEngine).Assembly.Location, replacedPath + ".new");
            File.Move(replacedPath + ".new", replacedPath, overwrite: true);
            Assert.Equal(0, Execute());
            File.Delete(replacedPath);
            Assert.Equal(0, Execute());
            Assert.Single(Directory.GetFiles(paths.CacheDirectory, "*.entry", SearchOption.AllDirectories));

            int Execute()
            {
                var engine = new TestEngine();
                var task = (ITask)Activator.CreateInstance(type)!;
                task.BuildEngine = engine;
                type.GetProperty(nameof(DeclaredIOTask.CacheDirectory))!.SetValue(task, paths.CacheDirectory);
                type.GetProperty(nameof(DeclaredIOTask.CacheEnabled))!.SetValue(task, true);
                type.GetProperty(nameof(DeclaredIOTestTask.Source))!.SetValue(task, paths.Source);
                type.GetProperty(nameof(DeclaredIOTestTask.Destination))!.SetValue(task, paths.Destination);
                Assert.True(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
                return (int)type.GetProperty(nameof(DeclaredIOTestTask.Executions))!.GetValue(task)!;
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Theory]
    [InlineData("warning", true)]
    [InlineData("getter-warning", true)]
    [InlineData("error", false)]
    [InlineData("false", false)]
    [InlineData("missing", false)]
    public void UncacheableExecutionsDoNotPublish(string behavior, bool success)
    {
        DeclaredIOTestTask task = Create();
        task.Behavior = behavior;
        Assert.Equal(success, task.Execute());
        Assert.Equal(1, task.Executions);
        Assert.Empty(Directory.GetFiles(task.CacheDirectory, "*.entry", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("missing-content")]
    [InlineData("corrupt-manifest")]
    [InlineData("malformed-entry")]
    public void CacheFailuresNeverFallBackToExecution(string failure)
    {
        DeclaredIOTestTask first = Create();
        Assert.True(first.Execute());
        byte[] output = File.ReadAllBytes(first.Destination);
        string[] blobs = Directory.GetFiles(first.CacheDirectory, "*.blob", SearchOption.AllDirectories);
        switch (failure)
        {
            case "missing-content":
                File.Delete(blobs.Single(path => File.ReadAllBytes(path).SequenceEqual(output)));
                break;
            case "corrupt-manifest":
                File.WriteAllBytes(blobs.Single(path => !File.ReadAllBytes(path).SequenceEqual(output)), new byte[] { 42 });
                break;
            case "malformed-entry":
                File.WriteAllBytes(Directory.GetFiles(first.CacheDirectory, "*.entry", SearchOption.AllDirectories).Single(), new byte[] { 42 });
                break;
        }

        File.WriteAllText(first.Destination, "untouched");
        DeclaredIOTestTask second = Create();
        Assert.False(second.Execute());
        Assert.Equal(0, second.Executions);
        Assert.Equal("untouched", File.ReadAllText(second.Destination));
        Assert.NotEmpty(((TestEngine)second.BuildEngine).Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDoesNotPublish(bool beforeExecution)
    {
        DeclaredIOTestTask task = Create();
        using var started = new ManualResetEventSlim();
        task.Started = started;
        task.Behavior = "wait";
        if (beforeExecution)
        {
            task.Cancel();
        }

        Task<bool> execution = Task.Run(task.Execute);
        if (!beforeExecution)
        {
            Assert.True(started.Wait(TimeSpan.FromSeconds(20)));
            task.Cancel();
        }

        Assert.False(await execution.WaitAsync(TimeSpan.FromSeconds(20)));
        task.Cancel();
        Assert.Equal(beforeExecution ? 0 : 1, task.Executions);
        Assert.Empty(Directory.Exists(task.CacheDirectory) ? Directory.GetFiles(task.CacheDirectory, "*.entry", SearchOption.AllDirectories) : Array.Empty<string>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [PlatformSpecific(TestPlatforms.Linux)]
    public void SymbolicLinkOutputsAreRejectedWithOrWithoutCaching(bool enabled)
    {
        DeclaredIOTestTask task = Create(enabled);
        task.Behavior = "symlink";
        Assert.False(task.Execute());
        Assert.Equal("input", File.ReadAllText(task.Source));
    }

    [Fact]
    [PlatformSpecific(TestPlatforms.Linux)]
    public void UnixPermissionsAreReplayed()
    {
        const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead;
        DeclaredIOTestTask first = Create();
        first.UnixMode = (int)Mode;
        Assert.True(first.Execute());
        File.Delete(first.Destination);
        DeclaredIOTestTask second = Create();
        second.UnixMode = (int)Mode;
        Assert.True(second.Execute());
        Assert.Equal(0, second.Executions);
        Assert.Equal(Mode, File.GetUnixFileMode(second.Destination));
    }

    [Fact]
    public void DeclarationSnapshotsInputsAndRejectsUnsupportedTypesAndMutation()
    {
        var declaration = new TaskDeclaration();
        var array = new[] { "before" };
        declaration.AddValue("setting", array);
        array[0] = "after";
        Assert.Throws<ArgumentException>(() => declaration.AddValue("unsupported", new object()));
        Assert.Throws<ArgumentException>(() => declaration.AddValue("nullable", (int?)1));
        declaration.Seal(_root);
        var codec = new TaskValueCodec(typeof(string[]));
        Assert.Equal(new[] { "before" }, Assert.IsType<string[]>(codec.Deserialize(declaration.Values.Single().Value)));
        Assert.Throws<InvalidOperationException>(() => declaration.AddValue("late", 1));

        var overlap = new TaskDeclaration();
        overlap.AddInputFile("same");
        overlap.AddOutputFile("same");
        Assert.Throws<ArgumentException>(() => overlap.Seal(_root));
    }

    public static IEnumerable<object?[]> Values()
    {
        yield return new object?[] { typeof(string), null };
        yield return new object?[] { typeof(string), "text\ud800%3B;" };
        yield return new object?[] { typeof(bool), true };
        yield return new object?[] { typeof(char), '\udfff' };
        yield return new object?[] { typeof(byte), byte.MaxValue };
        yield return new object?[] { typeof(sbyte), sbyte.MinValue };
        yield return new object?[] { typeof(short), short.MinValue };
        yield return new object?[] { typeof(ushort), ushort.MaxValue };
        yield return new object?[] { typeof(int), int.MinValue };
        yield return new object?[] { typeof(uint), uint.MaxValue };
        yield return new object?[] { typeof(long), long.MinValue };
        yield return new object?[] { typeof(ulong), ulong.MaxValue };
        yield return new object?[] { typeof(float), BitConverter.Int32BitsToSingle(unchecked((int)0xffc00001)) };
        yield return new object?[] { typeof(float), -0f };
        yield return new object?[] { typeof(float), float.PositiveInfinity };
        yield return new object?[] { typeof(double), BitConverter.Int64BitsToDouble(unchecked((long)0xfff8000000000001)) };
        yield return new object?[] { typeof(double), -0d };
        yield return new object?[] { typeof(decimal), 1.2500m };
        yield return new object?[] { typeof(DateTime), new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc) };
        yield return new object?[] { typeof(DateTime), new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Local) };
        yield return new object?[] { typeof(string[]), new string?[] { null, "", "text" } };
        yield return new object?[] { typeof(float[]), new[] { -0f, 1.25f, float.NaN } };
        yield return new object?[] { typeof(int[]), Array.Empty<int>() };
        yield return new object?[] { typeof(int[]), null };
    }

    [Theory]
    [MemberData(nameof(Values))]
    [MemberData(nameof(ArrayValues))]
    public void ValueCodecRoundTripsAndRejectsEveryTruncation(Type type, object? value)
    {
        var codec = new TaskValueCodec(type);
        byte[] encoded = codec.Serialize(value);
        object? actual = codec.Deserialize(encoded);
        Assert.Equal(value, actual);
        Assert.Equal(encoded, codec.Serialize(actual));
        if (value is float single)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(single), BitConverter.SingleToInt32Bits(Assert.IsType<float>(actual)));
        }

        if (value is double number)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(number), BitConverter.DoubleToInt64Bits(Assert.IsType<double>(actual)));
        }

        for (int length = 0; length < encoded.Length; length++)
        {
            Exception? failure = Record.Exception(() => codec.Deserialize(encoded.AsSpan(0, length).ToArray()));
            Assert.True(failure is EndOfStreamException or InvalidDataException, failure?.ToString());
        }

        Assert.Throws<InvalidDataException>(() => codec.Deserialize(encoded.Concat(new byte[] { 0 }).ToArray()));
    }

    public static IEnumerable<object?[]> ArrayValues()
    {
        var seen = new HashSet<Type>();
        foreach (object?[] row in Values())
        {
            Type type = Assert.IsAssignableFrom<Type>(row[0]);
            if (!type.IsArray && seen.Add(type))
            {
                Array array = Array.CreateInstance(type, 2);
                array.SetValue(row[1], 0);
                array.SetValue(row[1], 1);
                yield return new object?[] { type.MakeArrayType(), array };
            }
        }
    }

    [Fact]
    public void ItemAndItemArrayPreserveDefiningProjectAndLiteralMetadata()
    {
        string definingProject = Path.Combine(_root, "source.proj");
        var original = new TaskItem(ProjectCollection.Escape("literal%3B;item"));
        ((ITaskItem2)original).SetMetadataValueLiteral("Custom", "literal%3B;metadata");
        var source = new SourceItem(original, definingProject);
        var scalarCodec = new TaskValueCodec(typeof(ITaskItem));
        var arrayCodec = new TaskValueCodec(typeof(ITaskItem[]));
        ITaskItem item = Assert.IsAssignableFrom<ITaskItem>(scalarCodec.Deserialize(scalarCodec.Serialize(source)));
        ITaskItem?[] items = Assert.IsType<ITaskItem[]>(arrayCodec.Deserialize(arrayCodec.Serialize(new ITaskItem?[] { source, null })));
        Assert.Null(items[1]);
        foreach (ITaskItem value in new[] { item, Assert.IsAssignableFrom<ITaskItem>(items[0]) })
        {
            Assert.Equal(original.ItemSpec, value.ItemSpec);
            Assert.Equal(original.GetMetadata("Custom"), value.GetMetadata("Custom"));
            Assert.Equal("", value.GetMetadata("OriginalItemSpec"));
            Assert.Equal(definingProject, value.GetMetadata("DefiningProjectFullPath"));
            Assert.Equal("source", value.GetMetadata("DefiningProjectName"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackagePropsRespectOverrides(bool overridden)
    {
        using var collection = new ProjectCollection();
        string props = SecurityElement.Escape(Path.Combine(AppContext.BaseDirectory, "Microsoft.NET.Build.Caching.props"));
        string initial = overridden ? $"<PropertyGroup><DeclaredIOCacheDirectory>{SecurityElement.Escape(_root)}</DeclaredIOCacheDirectory></PropertyGroup>" : "";
        using var reader = XmlReader.Create(new StringReader($"<Project>{initial}<Import Project=\"{props}\" /></Project>"));
        var project = new Project(ProjectRootElement.Create(reader), null, null, collection);
        string expected = overridden ? _root : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify), "Microsoft", "DeclaredIOCache");
        Assert.Equal(Path.GetFullPath(expected), Path.GetFullPath(project.GetPropertyValue("DeclaredIOCacheDirectory")));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void MSBuildBindsFloatAndReplaysOutputs()
    {
        DeclaredIOTestTask paths = Create();
        using var collection = new ProjectCollection();
        string assembly = SecurityElement.Escape(Path.Combine(AppContext.BaseDirectory, "Microsoft.NET.Build.Caching.Tests.dll"));
        string xml = $"""
            <Project>
              <UsingTask TaskName="Microsoft.NET.Build.Caching.Tests.DeclaredIOTestTask" AssemblyFile="{assembly}" />
              <Target Name="Run">
                <DeclaredIOTestTask Source="{SecurityElement.Escape(paths.Source)}" Destination="{SecurityElement.Escape(paths.Destination)}"
                                   Value="1.25" Factor="2" CacheEnabled="true" CacheDirectory="{SecurityElement.Escape(paths.CacheDirectory)}">
                  <Output TaskParameter="Result" PropertyName="ScaledValue" />
                  <Output TaskParameter="Executions" PropertyName="ActualExecutions" />
                  <Output TaskParameter="Item" ItemName="ResultItem" />
                </DeclaredIOTestTask>
              </Target>
            </Project>
            """;
        using var reader = XmlReader.Create(new StringReader(xml));
        var project = new Project(ProjectRootElement.Create(reader), null, null, collection);
        using var manager = new BuildManager();
        var logger = new TestLogger();
        var parameters = new BuildParameters(collection) { MaxNodeCount = 1, EnableNodeReuse = false, Loggers = new[] { logger } };
        foreach (string executions in new[] { "1", "0" })
        {
            BuildResult result = manager.Build(parameters, new BuildRequestData(project.CreateProjectInstance(), new[] { "Run" }, null, BuildRequestDataFlags.ProvideProjectStateAfterBuild));
            Assert.True(result.OverallResult == BuildResultCode.Success, string.Join(Environment.NewLine, logger.Errors));
            Assert.Equal("2.5", result.ProjectStateAfterBuild.GetPropertyValue("ScaledValue"));
            Assert.Equal(executions, result.ProjectStateAfterBuild.GetPropertyValue("ActualExecutions"));
            Assert.Equal("value%3B;literal", result.ProjectStateAfterBuild.GetItems("ResultItem").Single().GetMetadataValue("Literal"));
        }
    }

    [Fact]
    public void WarningTrackerPreservesHostCapabilities()
    {
        var engine = new TestEngine();
        TrackingBuildEngine tracker = TrackingBuildEngine.Create(engine);
        Assert.False(tracker is IBuildEngine2);
        tracker.LogWarningEvent(new BuildWarningEventArgs("", "", "", 0, 0, 0, 0, "warning", "", ""));
        tracker.LogErrorEvent(new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "error", "", ""));
        Assert.True(tracker.HasWarnings);
        Assert.True(tracker.HasErrors);
        Assert.Single(engine.Errors);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (string path in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                {
                    File.SetAttributes(path, FileAttributes.Normal);
                }
            }

            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class TaskLoadContext(string basePath) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName) =>
            assemblyName.Name == typeof(DeclaredIOTask).Assembly.GetName().Name ? LoadFromAssemblyPath(basePath) : null;
    }

    private sealed class TestEngine : IBuildEngine
    {
        internal List<string> Errors { get; } = new();
        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => "";
        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "");
        public void LogWarningEvent(BuildWarningEventArgs e) { }
        public void LogMessageEvent(BuildMessageEventArgs e) { }
        public void LogCustomEvent(CustomBuildEventArgs e) { }
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) => throw new NotSupportedException();
    }

    private sealed class TestLogger : ILogger
    {
        internal List<string> Errors { get; } = new();
        public LoggerVerbosity Verbosity { get; set; }
        public string? Parameters { get; set; }
        public void Initialize(IEventSource eventSource) => eventSource.ErrorRaised += (_, e) => Errors.Add(e.Message ?? "");
        public void Shutdown() { }
    }

    private sealed class SourceItem(TaskItem item, string definingProject) : ITaskItem
    {
        public string ItemSpec { get => item.ItemSpec; set => item.ItemSpec = value; }
        public int MetadataCount => item.MetadataCount;
        public ICollection MetadataNames => item.MetadataNames;
        public string GetMetadata(string name) => name.Equals("DefiningProjectFullPath", StringComparison.OrdinalIgnoreCase) ? definingProject : item.GetMetadata(name);
        public void SetMetadata(string name, string value) => item.SetMetadata(name, value);
        public void RemoveMetadata(string name) => item.RemoveMetadata(name);
        public IDictionary CloneCustomMetadata() => item.CloneCustomMetadata();
        public void CopyMetadataTo(ITaskItem destinationItem) => item.CopyMetadataTo(destinationItem);
    }
}

public sealed class DeclaredIOTestTask : DeclaredIOTask
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public float Value { get; set; }
    public float Factor { get; set; }
    public bool MakeReadOnly { get; set; }
    public int UnixMode { get; set; } = -1;
    public string Behavior { get; set; } = "";
    public ManualResetEventSlim? Started { get; set; }
    [Output] public float Result { get; private set; }
    [Output] public string?[]? Values { get; private set; }
    [Output] public ITaskItem? Item { get; private set; }
    [Output] public int Executions { get; private set; }

    protected override void DescribeOperation(TaskDeclaration declaration)
    {
        declaration.AddInputFile(Source);
        declaration.AddOutputFile(Destination);
        declaration.AddValue(nameof(Value), Value);
        declaration.AddValue(nameof(Factor), Factor);
        declaration.AddValue(nameof(MakeReadOnly), MakeReadOnly);
        declaration.AddValue(nameof(UnixMode), UnixMode);
        declaration.AddOutputValue(nameof(Result), () =>
        {
            if (Behavior == "getter-warning")
            {
                Log.LogWarning("getter warning");
            }

            return Result;
        }, value => Result = value);
        declaration.AddOutputValue(nameof(Values), () => Values, value => Values = value);
        declaration.AddOutputItem(nameof(Item), () => Item, value => Item = value);
    }

    protected override bool ExecuteCore(CancellationToken cancellationToken)
    {
        Executions++;
        Started?.Set();
        if (Behavior == "wait")
        {
            cancellationToken.WaitHandle.WaitOne();
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (Behavior == "false")
        {
            return false;
        }

        if (Behavior == "missing")
        {
            return true;
        }

        if (Behavior == "warning")
        {
            Log.LogWarning("test warning");
        }

        if (Behavior == "error")
        {
            BuildEngine.LogErrorEvent(new BuildErrorEventArgs("", "", "", 0, 0, 0, 0, "test error", "", ""));
        }

        Result = Value * Factor;
        Values = new string?[] { "", null, "literal%3B;value" };
        Item = new TaskItem(ProjectCollection.Escape("item%3B;name"));
        ((ITaskItem2)Item).SetMetadataValueLiteral("Literal", "value%3B;literal");
        if (Behavior == "symlink")
        {
            File.CreateSymbolicLink(Destination, Source);
        }
        else
        {
            File.WriteAllText(Destination, File.ReadAllText(Source) + Result.ToString("R", CultureInfo.InvariantCulture));
        }

        if (MakeReadOnly)
        {
            File.SetAttributes(Destination, File.GetAttributes(Destination) | FileAttributes.ReadOnly);
        }

        if (UnixMode >= 0 && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Destination, (UnixFileMode)UnixMode);
        }

        return true;
    }
}
