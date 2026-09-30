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
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.DotNet.RemoteExecutor;
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
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ValueSerializationUsesExecutionScopedCacheMode(bool enabled, bool changeMode)
    {
        DeclaredIOTestTask task = Create(enabled);
        var item = new SourceItem(new TaskItem("input item"), "");
        task.AdditionalDeclarations = declaration =>
        {
            if (changeMode)
            {
                task.CacheEnabled = !enabled;
            }

            declaration.AddValue<ITaskItem>("setting", item);
        };

        Assert.True(task.Execute());
        Assert.Equal(1, task.Executions);
        Assert.Equal(enabled ? 1 : 0, item.SnapshotCount);
        Assert.Equal(enabled, Directory.Exists(task.CacheDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValueDeclarationsValidateEvenWithoutCaching(bool enabled)
    {
        DeclaredIOTestTask task = Create(enabled);
        task.AdditionalDeclarations = declaration =>
        {
            Assert.Throws<ArgumentException>(() => declaration.AddValue("unsupported", new object()));
            Assert.Throws<ArgumentException>(() => declaration.AddValue("nullable", (int?)1));
            Assert.Throws<ArgumentException>(() => declaration.AddValue("", 1));
            declaration.AddValue("unique", 1);
            Assert.Throws<ArgumentException>(() => declaration.AddValue("unique", 2));
            Assert.Throws<ArgumentException>(() => declaration.AddOutputValue("unsupported", () => new object(), _ => { }));
            Assert.Throws<ArgumentException>(() => declaration.AddOutputValue("", () => 1, _ => { }));
            declaration.AddOutputValue("unique", () => 1, _ => { });
            Assert.Throws<ArgumentException>(() => declaration.AddOutputValue("unique", () => 2, _ => { }));
        };

        Assert.True(task.Execute());
        Assert.Equal(1, task.Executions);
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

    [Theory]
    [InlineData("offset")]
    [InlineData("display")]
    [InlineData("rules")]
    public async Task TimeZoneFingerprintRetainsFullIdentity(string change)
    {
        TimeZoneInfo first = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.Zero, "Display", "Standard", "Daylight",
            Array.Empty<TimeZoneInfo.AdjustmentRule>());
        TimeZoneInfo.AdjustmentRule[] rules = change == "rules" ? new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 3, 1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1), 10, 1))
        } : Array.Empty<TimeZoneInfo.AdjustmentRule>();
        TimeZoneInfo second = TimeZoneInfo.CreateCustomTimeZone("Test", change == "offset" ? TimeSpan.FromHours(1) : TimeSpan.Zero,
            change == "display" ? "Different display" : "Display", "Standard", "Daylight", rules);
        Assert.Equal(first.Id, second.Id);
        byte[] firstHash = ExpectedHash(first);
        byte[] secondHash = ExpectedHash(second);
        Assert.NotEqual(firstHash, secondHash);

        await Task.WhenAll(Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            bool original = i % 2 == 0;
            TimeZoneInfo zone = original ? first : second;
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data);
            DeclaredIOTask.WriteTimeZoneFingerprint(writer, zone);
            Assert.Equal(SHA256.HashSizeInBytes, data.Length);
            Assert.Equal(original ? firstHash : secondHash, data.ToArray());
            data.Position = 0;
            DeclaredIOTask.WriteTimeZoneFingerprint(writer, zone);
            Assert.Equal(original ? firstHash : secondHash, data.ToArray());
            data.Position = 0;
            DeclaredIOTask.WriteTimeZoneFingerprint(writer, TimeZoneInfo.FromSerializedString(zone.ToSerializedString()));
            Assert.Equal(original ? firstHash : secondHash, data.ToArray());
        })));

        static byte[] ExpectedHash(TimeZoneInfo zone)
        {
            using var data = new MemoryStream();
            using var writer = new BinaryWriter(data);
            TaskValueCodec.WriteString(writer, zone.ToSerializedString());
            return SHA256.HashData(data.ToArray());
        }
    }

    [ConditionalFact(typeof(RemoteExecutor), nameof(RemoteExecutor.IsSupported))]
    [PlatformSpecific(TestPlatforms.Linux)]
    public void LocalTimeZoneChangesInvalidateTaskCache()
    {
        using RemoteInvokeHandle process = RemoteExecutor.Invoke(static () =>
        {
            using var tests = new DeclaredIOTaskTests();
            foreach ((string zone, int executions) in new[]
            {
                ("UTC", 1), ("UTC", 0), ("America/Los_Angeles", 1), ("America/Los_Angeles", 0), ("UTC", 0)
            })
            {
                Environment.SetEnvironmentVariable("TZ", zone);
                TimeZoneInfo.ClearCachedData();
                Assert.Equal(zone, TimeZoneInfo.Local.Id);
                DeclaredIOTestTask task = tests.Create();
                Assert.True(task.Execute(), string.Join(Environment.NewLine, ((TestEngine)task.BuildEngine).Errors));
                Assert.Equal(executions, task.Executions);
                Assert.Equal(2.5f, task.Result);
            }

            Assert.Equal(2, Directory.GetFiles(tests._root, "*.entry", SearchOption.AllDirectories).Length);
        });
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

    [ConditionalTheory(typeof(RuntimeFeature), nameof(RuntimeFeature.IsDynamicCodeSupported))]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadedIntermediateModuleIdentityDistinguishesTaskImplementations(bool includeBridge)
    {
        DeclaredIOTestTask paths = Create();
        (byte[] firstImage, Type firstBase) = CreateAssembly("IntermediateTask", typeof(DeclaredIOTestTask), "original", includeBridge: false);
        (byte[] secondImage, _) = CreateAssembly("IntermediateTask", typeof(DeclaredIOTestTask), "updated", includeBridge: false);
        (byte[] leafImage, _) = CreateAssembly("LeafTask", firstBase, suffix: null, includeBridge);
        var firstContext = new IntermediateTaskLoadContext(firstImage);
        var secondContext = new IntermediateTaskLoadContext(secondImage);
        try
        {
            Type first = LoadTask(firstContext);
            Type second = LoadTask(secondContext);
            Assert.Equal(first.FullName, second.FullName);
            Assert.Equal(first.Assembly.FullName, second.Assembly.FullName);
            Assert.Equal(first.Module.ModuleVersionId, second.Module.ModuleVersionId);
            Assert.NotEqual(
                firstContext.Assemblies.Single(assembly => assembly.GetName().Name == "IntermediateTask").ManifestModule.ModuleVersionId,
                secondContext.Assemblies.Single(assembly => assembly.GetName().Name == "IntermediateTask").ManifestModule.ModuleVersionId);

            foreach ((Type type, string suffix, int executions) in new[] { (first, "original", 1), (first, "original", 0), (second, "updated", 1), (second, "updated", 0) })
            {
                File.Delete(paths.Destination);
                var engine = new TestEngine();
                var task = (DeclaredIOTestTask)Activator.CreateInstance(type)!;
                task.BuildEngine = engine;
                task.CacheDirectory = paths.CacheDirectory;
                task.CacheEnabled = true;
                task.Source = paths.Source;
                task.Destination = paths.Destination;
                task.Value = paths.Value;
                task.Factor = paths.Factor;
                Assert.True(task.Execute(), string.Join(Environment.NewLine, engine.Errors));
                Assert.Equal(executions, task.Executions);
                Assert.Equal("input2.5" + suffix, File.ReadAllText(task.Destination));
                Assert.Equal(2.5f, task.Result);
            }

            Assert.Equal(2, Directory.GetFiles(paths.CacheDirectory, "*.entry", SearchOption.AllDirectories).Length);
        }
        finally
        {
            firstContext.Unload();
            secondContext.Unload();
        }

        Type LoadTask(AssemblyLoadContext context)
        {
            using var stream = new MemoryStream(leafImage);
            return context.LoadFromStream(stream).GetType("LeafTask.Task", throwOnError: true)!;
        }

        static (byte[] Image, Type Type) CreateAssembly(string name, Type baseType, string? suffix, bool includeBridge)
        {
            var assembly = new PersistedAssemblyBuilder(new AssemblyName(name), typeof(object).Assembly);
            ModuleBuilder module = assembly.DefineDynamicModule(name);
            if (includeBridge)
            {
                TypeBuilder bridge = module.DefineType(name + ".Bridge", TypeAttributes.Public, baseType);
                bridge.DefineDefaultConstructor(MethodAttributes.Public);
                baseType = bridge.CreateType()!;
            }

            TypeBuilder type = module.DefineType(name + ".Task", TypeAttributes.Public, baseType);
            type.DefineDefaultConstructor(MethodAttributes.Public);
            if (suffix is not null)
            {
                MethodBuilder execute = type.DefineMethod("ExecuteCore", MethodAttributes.Family | MethodAttributes.Virtual, typeof(bool), new[] { typeof(CancellationToken) });
                ILGenerator il = execute.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Call, baseType.GetMethod("ExecuteCore", BindingFlags.Instance | BindingFlags.NonPublic)!);
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, baseType.GetProperty(nameof(DeclaredIOTestTask.Destination))!.GetMethod!);
                il.Emit(OpCodes.Ldstr, suffix);
                il.Emit(OpCodes.Call, typeof(File).GetMethod(nameof(File.AppendAllText), new[] { typeof(string), typeof(string) })!);
                il.Emit(OpCodes.Ldc_I4_1);
                il.Emit(OpCodes.Ret);
            }

            Type result = type.CreateType()!;
            using var stream = new MemoryStream();
            assembly.Save(stream);
            return (stream.ToArray(), result);
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
    [InlineData("getter-error", false)]
    [InlineData("error", false)]
    [InlineData("false", false)]
    [InlineData("missing", false)]
    public void UncacheableExecutionsDoNotPublish(string behavior, bool success)
    {
        DeclaredIOTestTask task = Create();
        task.Behavior = behavior;
        Assert.Equal(success, task.Execute());
        Assert.Equal(1, task.Executions);
        Assert.False(Directory.Exists(task.CacheDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingInputReportsDeclaredPath(bool enabled)
    {
        DeclaredIOTestTask task = Create(enabled);
        File.Delete(task.Source);
        Assert.False(task.Execute());
        Assert.Equal(0, task.Executions);
        Assert.Contains($"Declared input file '{task.Source}' does not exist.", Assert.Single(((TestEngine)task.BuildEngine).Errors));
        Assert.False(Directory.Exists(task.CacheDirectory));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationComparesWinningManifest(bool conflict)
    {
        DeclaredIOTestTask first = Create();
        DeclaredIOTestTask winner = Create();
        winner.AdditionalDeclarations = declaration => declaration.AddOutputValue("Race", () => 1, _ => { });
        first.AdditionalDeclarations = declaration => declaration.AddOutputValue("Race", () =>
        {
            Assert.True(winner.Execute());
            return conflict ? 2 : 1;
        }, _ => { });

        Assert.Equal(!conflict, first.Execute());
        Assert.Equal(1, first.Executions);
        Assert.Equal(1, winner.Executions);
        Assert.Single(Directory.GetFiles(first.CacheDirectory, "*.entry", SearchOption.AllDirectories));
        if (conflict)
        {
            Assert.Contains("different result", Assert.Single(((TestEngine)first.BuildEngine).Errors));
        }

        DeclaredIOTestTask replay = Create();
        int value = 0;
        replay.AdditionalDeclarations = declaration => declaration.AddOutputValue("Race", () => throw new InvalidOperationException(), (int result) => value = result);
        Assert.True(replay.Execute());
        Assert.Equal(0, replay.Executions);
        Assert.Equal(1, value);
    }

    [Fact]
    [PlatformSpecific(TestPlatforms.Linux)]
    public void ReplayReplacesSymbolicLinkWithoutChangingTarget()
    {
        DeclaredIOTestTask first = Create();
        Assert.True(first.Execute());
        string expected = File.ReadAllText(first.Destination);
        File.Delete(first.Destination);
        File.CreateSymbolicLink(first.Destination, first.Source);

        DeclaredIOTestTask replay = Create();
        Assert.True(replay.Execute());
        Assert.Equal(0, replay.Executions);
        Assert.Equal(expected, File.ReadAllText(replay.Destination));
        Assert.Equal("input", File.ReadAllText(replay.Source));
        Assert.Null(new FileInfo(replay.Destination).LinkTarget);
    }

    [Fact]
    [PlatformSpecific(TestPlatforms.Linux)]
    public void ReplayFileCreationIsPrivateBeforeCopy()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "private-output");
        using FileStream output = DiskCacheFileSystem.CreatePrivateFile(path, FileAccess.Write);
        Assert.Equal((UnixFileMode)0, File.GetUnixFileMode(path) & ~(UnixFileMode.UserRead | UnixFileMode.UserWrite));
        output.WriteByte(42);
        Assert.Throws<IOException>(() => DiskCacheFileSystem.CreatePrivateFile(path, FileAccess.Write));
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

    [Theory]
    [PlatformSpecific(TestPlatforms.Linux)]
    [InlineData(UnixFileMode.UserRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserExecute | UnixFileMode.GroupRead)]
    public void UnixPermissionsAreReplayed(UnixFileMode mode)
    {
        DeclaredIOTestTask first = Create();
        first.UnixMode = (int)mode;
        Assert.True(first.Execute());
        File.Delete(first.Destination);
        DeclaredIOTestTask second = Create();
        second.UnixMode = (int)mode;
        Assert.True(second.Execute());
        Assert.Equal(0, second.Executions);
        Assert.Equal(mode, File.GetUnixFileMode(second.Destination));
    }

    [Fact]
    public void DeclarationSnapshotsInputsAndRejectsUnsupportedTypesAndMutation()
    {
        var declaration = new TaskDeclaration(cacheEnabled: true);
        var array = new[] { "before" };
        declaration.AddValue("setting", array);
        array[0] = "after";
        var item = new TaskItem("before");
        item.SetMetadata("Custom", "before");
        ITaskItem[] items = { item };
        declaration.AddValue("items", items);
        item.ItemSpec = "after";
        item.SetMetadata("Custom", "after");
        items[0] = new TaskItem("replacement");
        Assert.Throws<ArgumentException>(() => declaration.AddValue("unsupported", new object()));
        Assert.Throws<ArgumentException>(() => declaration.AddValue("nullable", (int?)1));
        declaration.Seal(_root);
        var codec = new TaskValueCodec(typeof(string[]));
        Assert.Equal(new[] { "before" }, Assert.IsType<string[]>(codec.Deserialize(declaration.Values.Single(value => value.Key == "setting").Value)));
        var itemCodec = new TaskValueCodec(typeof(ITaskItem[]));
        ITaskItem restored = Assert.Single(Assert.IsType<ITaskItem[]>(itemCodec.Deserialize(declaration.Values.Single(value => value.Key == "items").Value)));
        Assert.Equal("before", restored.ItemSpec);
        Assert.Equal("before", restored.GetMetadata("Custom"));
        Assert.Throws<InvalidOperationException>(() => declaration.AddValue("late", 1));

        var overlap = new TaskDeclaration(cacheEnabled: true);
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

    [Theory]
    [InlineData("literal%253B%3Bitem", false)]
    [InlineData("literal%253B%3Bitem", true)]
    [InlineData("a%5Cb", false)]
    [InlineData("a%5Cb", true)]
    [InlineData("a%3Bb%3bc", false)]
    [InlineData("a%3Bb%3bc", true)]
    [InlineData("", false)]
    [InlineData("", true)]
    public void ItemAndItemArrayPreserveEscapingAndDefiningProject(string escapedItemSpec, bool supportsEscaping)
    {
        string definingProject = Path.Combine(_root, "literal%3B;directory", "source.proj");
        var original = new TaskItem(escapedItemSpec);
        ((ITaskItem2)original).SetMetadataValueLiteral("Custom", "literal%3B;metadata");
        var literalSource = new SourceItem(original, definingProject);
        ITaskItem source = supportsEscaping ? new TaskItem(literalSource) : literalSource;
        if (source is ITaskItem2 escapedSource)
        {
            escapedSource.EvaluatedIncludeEscaped = escapedItemSpec;
        }

        source.SetMetadata("List", "A;B");
        source.SetMetadata("Escaped", "a%3Bb%3bc");
        var scalarCodec = new TaskValueCodec(typeof(ITaskItem));
        var arrayCodec = new TaskValueCodec(typeof(ITaskItem[]));
        byte[] encoded = scalarCodec.Serialize(source);
        ITaskItem item = Assert.IsAssignableFrom<ITaskItem>(scalarCodec.Deserialize(encoded));
        ITaskItem?[] items = Assert.IsType<ITaskItem[]>(arrayCodec.Deserialize(arrayCodec.Serialize(new ITaskItem?[] { source, null })));
        Assert.Null(items[1]);
        foreach (ITaskItem value in new[] { item, Assert.IsAssignableFrom<ITaskItem>(items[0]) })
        {
            var escapedValue = Assert.IsAssignableFrom<ITaskItem2>(value);
            string expectedSpec = source is ITaskItem2 source2 ? source2.EvaluatedIncludeEscaped : ProjectCollection.Escape(source.ItemSpec);
            Assert.Equal(source.ItemSpec, value.ItemSpec);
            Assert.Equal(expectedSpec, escapedValue.EvaluatedIncludeEscaped);
            Assert.Equal(source.CloneCustomMetadata().Count, value.CloneCustomMetadata().Count);
            foreach (string name in new[] { "Custom", "List", "Escaped" })
            {
                string expectedMetadata = source is ITaskItem2 sourceWithEscaping ? sourceWithEscaping.GetMetadataValueEscaped(name) : ProjectCollection.Escape(source.GetMetadata(name));
                Assert.Equal(source.GetMetadata(name), value.GetMetadata(name));
                Assert.Equal(expectedMetadata, escapedValue.GetMetadataValueEscaped(name));
            }

            Assert.Equal("", value.GetMetadata("OriginalItemSpec"));
            Assert.Equal(definingProject, value.GetMetadata("DefiningProjectFullPath"));
            Assert.Equal("source", value.GetMetadata("DefiningProjectName"));
            Assert.Equal(encoded, scalarCodec.Serialize(value));
        }

        if (supportsEscaping)
        {
            ((ITaskItem2)item).SetMetadataValueLiteral("List", "A;B");
            Assert.NotEqual(encoded, scalarCodec.Serialize(item));
        }

        for (int length = 0; length < encoded.Length; length++)
        {
            Exception? failure = Record.Exception(() => scalarCodec.Deserialize(encoded.AsSpan(0, length).ToArray()));
            Assert.True(failure is EndOfStreamException or InvalidDataException, failure?.ToString());
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
                <ItemGroup>
                  <ExpandedList Include="%(ResultItem.List)" />
                  <LiteralList Include="%(ResultItem.LiteralList)" />
                </ItemGroup>
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
            Assert.Equal(new[] { "A", "B" }, result.ProjectStateAfterBuild.GetItems("ExpandedList").Select(item => item.EvaluatedInclude));
            Assert.Equal("A;B", Assert.Single(result.ProjectStateAfterBuild.GetItems("LiteralList")).EvaluatedInclude);
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

    private sealed class IntermediateTaskLoadContext(byte[] image) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name != "IntermediateTask")
            {
                return null;
            }

            using var stream = new MemoryStream(image);
            return LoadFromStream(stream);
        }
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
        internal int SnapshotCount { get; private set; }
        public string ItemSpec { get => item.ItemSpec; set => item.ItemSpec = value; }
        public int MetadataCount => item.MetadataCount;
        public ICollection MetadataNames => item.MetadataNames;
        public string GetMetadata(string name) => name.Equals("DefiningProjectFullPath", StringComparison.OrdinalIgnoreCase) ? definingProject : item.GetMetadata(name);
        public void SetMetadata(string name, string value) => item.SetMetadata(name, value);
        public void RemoveMetadata(string name) => item.RemoveMetadata(name);
        public IDictionary CloneCustomMetadata()
        {
            SnapshotCount++;
            return item.CloneCustomMetadata();
        }
        public void CopyMetadataTo(ITaskItem destinationItem)
        {
            foreach (DictionaryEntry entry in item.CloneCustomMetadata())
            {
                destinationItem.SetMetadata((string)entry.Key, ProjectCollection.Escape((string)entry.Value!));
            }
        }
    }
}

public class DeclaredIOTestTask : DeclaredIOTask
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public float Value { get; set; }
    public float Factor { get; set; }
    public bool MakeReadOnly { get; set; }
    public int UnixMode { get; set; } = -1;
    public string Behavior { get; set; } = "";
    public ManualResetEventSlim? Started { get; set; }
    public Action<TaskDeclaration>? AdditionalDeclarations { get; set; }
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
            else if (Behavior == "getter-error")
            {
                Log.LogError("getter error");
            }

            return Result;
        }, value => Result = value);
        declaration.AddOutputValue(nameof(Values), () => Values, value => Values = value);
        declaration.AddOutputItem(nameof(Item), () => Item, value => Item = value);
        AdditionalDeclarations?.Invoke(declaration);
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
        Item.SetMetadata("List", "A;B");
        ((ITaskItem2)Item).SetMetadataValueLiteral("LiteralList", "A;B");
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
