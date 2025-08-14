// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Mono.Linker.Tests.Cases.Expectations.Assertions;
using Mono.Linker.Tests.Cases.Expectations.Helpers;
using Mono.Linker.Tests.Cases.Statics;

namespace Mono.Linker.Tests.Cases.DataFlow
{
    [ExpectedNoWarnings]
    [SkipKeptItemsValidation]
    public class RequiresIsolated
    {
        public static void Main()
        {
            new C();
        }


        [RequiresUnreferencedCode("A")]
        class A;

        // The new constraint isn't detected.
        class BGen<T> where T : new();

        class C : BGen<A>
        {
            public C() { }
        }
    }
}
