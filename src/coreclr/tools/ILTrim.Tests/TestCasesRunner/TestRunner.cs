// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.DotNet.XUnitExtensions;
using Xunit.Sdk;
using Mono.Linker.Tests.Extensions;

namespace Mono.Linker.Tests.TestCasesRunner
{
    public partial class TestRunner
    {
        private TrimmingCustomizations? _trimmingCustomizations;

        partial void IgnoreTest(string reason)
        {
            throw new SkipTestException(reason);
        }

        private partial IEnumerable<string>? GetAdditionalDefines() => null;

        private static T GetResultOfTaskThatMakesAssertions<T>(Task<T> task)
        {
            try
            {
                return task.Result;
            }
            catch (AggregateException e)
            {
                if (e.InnerException is XunitException)
                    throw e.InnerException;

                throw;
            }
        }

        protected virtual partial TrimmingCustomizations? CustomizeTrimming(TrimmingDriver linker, TestCaseMetadataProvider metadataProvider)
        {
            _trimmingCustomizations = new TrimmingCustomizations
            {
                DependencyRecorder = new TestDependencyRecorder(),
            };
            return _trimmingCustomizations;
        }

        protected partial void AddDumpDependenciesOptions(TestCaseLinkerOptions caseDefinedOptions, ManagedCompilationResult compilationResult, TrimmingArgumentBuilder builder, TestCaseMetadataProvider metadataProvider)
        {
            Debug.Assert(_trimmingCustomizations is not null);
            NPath dependencyFilePath = compilationResult.InputAssemblyPath.Parent.Combine("iltrim-dependencies.dgml");
            _trimmingCustomizations.DependencyFilePath = dependencyFilePath;
            _trimmingCustomizations.InputAssemblyPath = compilationResult.InputAssemblyPath;

            builder.AddAdditionalArgument("--dump-dependencies", [compilationResult.InputAssemblyPath.FileNameWithoutExtension]);
            builder.AddAdditionalArgument("--dependencies-file", [dependencyFilePath]);
        }

        static partial void AddOutputDirectory(TestCaseSandbox sandbox, ManagedCompilationResult compilationResult, TrimmingArgumentBuilder builder)
        {
            builder.AddOutputDirectory(sandbox.OutputDirectory);
        }

        static partial void AddInputReference(NPath inputReference, TrimmingArgumentBuilder builder)
        {
            builder.AddReference(inputReference);
        }
    }
}
