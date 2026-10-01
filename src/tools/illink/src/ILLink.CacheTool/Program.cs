// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;

namespace ILLink.CacheTool;

internal static class Program
{
    private static int Main(string[] args)
    {
        var command = new RootCommand("Experimental ILLink task cache maintenance. Commands and cache formats may change without notice.");
        return command.Parse(args).Invoke(new() { EnableDefaultExceptionHandler = false });
    }
}
