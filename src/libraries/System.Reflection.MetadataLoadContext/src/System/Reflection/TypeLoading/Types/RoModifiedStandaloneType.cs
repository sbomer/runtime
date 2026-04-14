// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.TypeLoading;
using System.Diagnostics.CodeAnalysis;

namespace System.Reflection
{
    /// <summary>
    /// Standard modified types that don't have references to other types.
    /// </summary>
    internal sealed partial class RoModifiedStandaloneType : RoModifiedType
    {
        [RequiresUnreferencedCode("Types might be removed")]
        public RoModifiedStandaloneType(RoType delegatingType) : base(delegatingType) { }
    }
}
