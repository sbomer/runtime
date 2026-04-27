// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System.Reflection.TypeLoading
{
    //
    // This interface is implemented by the MethodDecoder structs that are embedded in RoDefinitionMethod and RoDefinitionConstructor.
    // The MethodDecoder struct encapsulates the underlying metadata provider for both methods and constructors.
    //
    internal interface IMethodDecoder
    {
        RoModule GetRoModule();

        string ComputeName();
        MethodAttributes ComputeAttributes();
        CallingConventions ComputeCallingConvention();
        MethodImplAttributes ComputeMethodImplementationFlags();
        int MetadataToken { get; }

        int ComputeGenericParameterCount();
        // RUC is on the interface method because EcmaMethodDecoder is a struct and
        // type-level RUC is not supported on structs (https://github.com/dotnet/runtime/issues/90115).
        [RequiresUnreferencedCode(Helpers.TrimmingRequiresUnreferencedCodeMessage)]
        RoType[] ComputeGenericArgumentsOrParameters();

        [RequiresUnreferencedCode(Helpers.TrimmingRequiresUnreferencedCodeMessage)]
        IEnumerable<CustomAttributeData> ComputeTrueCustomAttributes();
        DllImportAttribute ComputeDllImportAttribute();

        [RequiresUnreferencedCode(Helpers.TrimmingRequiresUnreferencedCodeMessage)]
        MethodSig<RoParameter> SpecializeMethodSig(IRoMethodBase member);
        [RequiresUnreferencedCode(Helpers.TrimmingRequiresUnreferencedCodeMessage)]
        MethodBody? SpecializeMethodBody(IRoMethodBase owner);
        MethodSig<string> SpecializeMethodSigStrings(in TypeContext typeContext);
    }
}
