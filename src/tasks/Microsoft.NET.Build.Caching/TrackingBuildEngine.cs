// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Build.Framework;

namespace Microsoft.NET.Build.Caching;

internal class TrackingBuildEngine(IBuildEngine engine) : IBuildEngine
{
    private int _errors;
    private int _warnings;

    protected IBuildEngine Engine { get; } = engine;
    internal bool HasErrors => Volatile.Read(ref _errors) != 0;
    internal bool HasWarnings => Volatile.Read(ref _warnings) != 0;

    // Preserve interface availability instead of advertising capabilities the host does not have.
    internal static TrackingBuildEngine Create(IBuildEngine engine) => engine switch
    {
        IBuildEngine10 => new Engine10(engine),
        IBuildEngine9 => new Engine9(engine),
        IBuildEngine8 => new Engine8(engine),
        IBuildEngine7 => new Engine7(engine),
        IBuildEngine6 => new Engine6(engine),
        IBuildEngine5 => new Engine5(engine),
        IBuildEngine4 => new Engine4(engine),
        IBuildEngine3 => new Engine3(engine),
        IBuildEngine2 => new Engine2(engine),
        _ => new TrackingBuildEngine(engine)
    };

    public bool ContinueOnError => Engine.ContinueOnError;
    public int LineNumberOfTaskNode => Engine.LineNumberOfTaskNode;
    public int ColumnNumberOfTaskNode => Engine.ColumnNumberOfTaskNode;
    public string ProjectFileOfTaskNode => Engine.ProjectFileOfTaskNode;
    public void LogErrorEvent(BuildErrorEventArgs e) { Interlocked.Increment(ref _errors); Engine.LogErrorEvent(e); }
    public void LogWarningEvent(BuildWarningEventArgs e) { Interlocked.Increment(ref _warnings); Engine.LogWarningEvent(e); }
    public void LogMessageEvent(BuildMessageEventArgs e) => Engine.LogMessageEvent(e);
    public void LogCustomEvent(CustomBuildEventArgs e) => Engine.LogCustomEvent(e);
    public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs) =>
        Engine.BuildProjectFile(projectFileName, targetNames, globalProperties, targetOutputs);

    private class Engine2(IBuildEngine engine) : TrackingBuildEngine(engine), IBuildEngine2
    {
        private IBuildEngine2 Inner => (IBuildEngine2)Engine;
        public bool IsRunningMultipleNodes => Inner.IsRunningMultipleNodes;
        public bool BuildProjectFile(string projectFileName, string[] targetNames, IDictionary globalProperties, IDictionary targetOutputs, string toolsVersion) =>
            Inner.BuildProjectFile(projectFileName, targetNames, globalProperties, targetOutputs, toolsVersion);
        public bool BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IDictionary[] targetOutputsPerProject, string[] toolsVersion, bool useResultsCache, bool unloadProjectsOnCompletion) =>
            Inner.BuildProjectFilesInParallel(projectFileNames, targetNames, globalProperties, targetOutputsPerProject, toolsVersion, useResultsCache, unloadProjectsOnCompletion);
    }

    private class Engine3(IBuildEngine engine) : Engine2(engine), IBuildEngine3
    {
        private IBuildEngine3 Inner => (IBuildEngine3)Engine;
        public BuildEngineResult BuildProjectFilesInParallel(string[] projectFileNames, string[] targetNames, IDictionary[] globalProperties, IList<string>[] removeGlobalProperties, string[] toolsVersion, bool returnTargetOutputs) =>
            Inner.BuildProjectFilesInParallel(projectFileNames, targetNames, globalProperties, removeGlobalProperties, toolsVersion, returnTargetOutputs);
        public void Yield() => Inner.Yield();
        public void Reacquire() => Inner.Reacquire();
    }

    private class Engine4(IBuildEngine engine) : Engine3(engine), IBuildEngine4
    {
        private IBuildEngine4 Inner => (IBuildEngine4)Engine;
        public void RegisterTaskObject(object key, object obj, RegisteredTaskObjectLifetime lifetime, bool allowEarlyCollection) => Inner.RegisterTaskObject(key, obj, lifetime, allowEarlyCollection);
        public object GetRegisteredTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => Inner.GetRegisteredTaskObject(key, lifetime);
        public object UnregisterTaskObject(object key, RegisteredTaskObjectLifetime lifetime) => Inner.UnregisterTaskObject(key, lifetime);
    }

    private class Engine5(IBuildEngine engine) : Engine4(engine), IBuildEngine5
    {
        public void LogTelemetry(string eventName, IDictionary<string, string> properties) => ((IBuildEngine5)Engine).LogTelemetry(eventName, properties);
    }

    private class Engine6(IBuildEngine engine) : Engine5(engine), IBuildEngine6
    {
        public IReadOnlyDictionary<string, string> GetGlobalProperties() => ((IBuildEngine6)Engine).GetGlobalProperties();
    }

    private class Engine7(IBuildEngine engine) : Engine6(engine), IBuildEngine7
    {
        public bool AllowFailureWithoutError { get => ((IBuildEngine7)Engine).AllowFailureWithoutError; set => ((IBuildEngine7)Engine).AllowFailureWithoutError = value; }
    }

    private class Engine8(IBuildEngine engine) : Engine7(engine), IBuildEngine8
    {
        public bool ShouldTreatWarningAsError(string warningCode) => ((IBuildEngine8)Engine).ShouldTreatWarningAsError(warningCode);
    }

    private class Engine9(IBuildEngine engine) : Engine8(engine), IBuildEngine9
    {
        public int RequestCores(int requestedCores) => ((IBuildEngine9)Engine).RequestCores(requestedCores);
        public void ReleaseCores(int coresToRelease) => ((IBuildEngine9)Engine).ReleaseCores(coresToRelease);
    }

    private sealed class Engine10(IBuildEngine engine) : Engine9(engine), IBuildEngine10
    {
        public EngineServices EngineServices => ((IBuildEngine10)Engine).EngineServices;
    }
}
