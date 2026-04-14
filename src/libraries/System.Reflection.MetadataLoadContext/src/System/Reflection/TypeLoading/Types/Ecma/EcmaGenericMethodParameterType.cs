// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.TypeLoading.Ecma
{
    /// <summary>
    /// RoTypes that return true for IsGenericMethodParameter and get its metadata from a PEReader.
    /// </summary>
    internal sealed class EcmaGenericMethodParameterType : EcmaGenericParameterType
    {
        [RequiresUnreferencedCode("Types might be removed")]
        internal EcmaGenericMethodParameterType(GenericParameterHandle handle, EcmaModule module)
            : base(handle, module)
        {
            Debug.Assert(!handle.IsNil);
        }

        public sealed override bool IsGenericTypeParameter => false;
        public sealed override bool IsGenericMethodParameter => true;

        protected sealed override RoType ComputeDeclaringType() => GetRoDeclaringMethod().GetRoDeclaringType();

        public sealed override MethodBase DeclaringMethod => GetRoDeclaringMethod();
        [RequiresUnreferencedCode("Members might be removed")]
        private RoMethod GetRoDeclaringMethod() => _lazyDeclaringMethod ??= ComputeDeclaringMethod();
        [RequiresUnreferencedCode("Types might be removed")]
        private RoMethod ComputeDeclaringMethod() => ((MethodDefinitionHandle)(GenericParameter.Parent)).ResolveMethod<RoMethod>(GetEcmaModule(), default);
        private volatile RoMethod? _lazyDeclaringMethod;

        protected sealed override TypeContext TypeContext => GetRoDeclaringMethod().TypeContext;
    }
}
