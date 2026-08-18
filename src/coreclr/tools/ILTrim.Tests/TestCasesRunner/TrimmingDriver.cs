// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO;

using Mono.Linker;

namespace Mono.Linker.Tests.TestCasesRunner
{
    public class TrimmingDriver
    {
        public TrimmingResults Trim(string[] args, TrimmingCustomizations? customizations, TrimmingTestLogger logger)
        {
            if (customizations?.DependencyFilePath is string dependencyFilePath)
                File.Delete(dependencyFilePath);

            Driver.ProcessResponseFile(args, out var queue);
            using var driver = new Driver(queue);
            var results = new TrimmingResults(driver.Run(logger));
            customizations?.DependencyRecorder?.Load(
                customizations.DependencyFilePath,
                customizations.InputAssemblyPath);
            return results;
        }
    }
}
