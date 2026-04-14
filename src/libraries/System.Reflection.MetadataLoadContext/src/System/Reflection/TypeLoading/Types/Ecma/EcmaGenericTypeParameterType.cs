// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;
using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.TypeLoading.Ecma
{
    /// <summary>
    /// RoTypes that return true for IsGenericTypeParameter and get its metadata from a PEReader.
    /// </summary>
    internal sealed class EcmaGenericTypeParameterType : EcmaGenericParameterType
    {
        [RequiresUnreferencedCode("Types might be removed")]
        internal EcmaGenericTypeParameterType(GenericParameterHandle handle, EcmaModule module)
            : base(handle, module)
        {
        }

        public sealed override bool IsGenericTypeParameter => true;
        public sealed override bool IsGenericMethodParameter => false;

        [RequiresUnreferencedCode("Types might be removed")]
        protected sealed override RoType? ComputeDeclaringType()
        {
            TypeDefinitionHandle declaringTypeHandle = (TypeDefinitionHandle)(GenericParameter.Parent);
            EcmaDefinitionType declaringType = declaringTypeHandle.ResolveTypeDef(GetEcmaModule());
            return declaringType;
        }

        public sealed override MethodBase? DeclaringMethod => null;

        protected sealed override TypeContext TypeContext => ((RoInstantiationProviderType)GetRoDeclaringType()!).Instantiation.ToTypeContext();
    }
}
