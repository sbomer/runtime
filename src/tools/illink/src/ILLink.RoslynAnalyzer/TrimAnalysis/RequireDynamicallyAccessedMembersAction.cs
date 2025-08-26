// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using ILLink.RoslynAnalyzer;
using ILLink.RoslynAnalyzer.TrimAnalysis;
using ILLink.Shared.TypeSystemProxy;

namespace ILLink.Shared.TrimAnalysis
{
    internal partial struct RequireDynamicallyAccessedMembersAction
    {
        readonly ReflectionAccessAnalyzer _reflectionAccessAnalyzer;
        readonly TypeNameResolver _typeNameResolver;
#pragma warning disable CA1822 // Mark members as static - the other partial implementations might need to be instance methods
#pragma warning disable IDE0060 // Unused parameters - should be removed once methods are actually implemented

        public RequireDynamicallyAccessedMembersAction(
            TypeNameResolver typeNameResolver,
            in DiagnosticContext diagnosticContext,
            ReflectionAccessAnalyzer reflectionAccessAnalyzer)
        {
            _typeNameResolver = typeNameResolver;
            _diagnosticContext = diagnosticContext;
            _reflectionAccessAnalyzer = reflectionAccessAnalyzer;
        }

        public partial bool TryResolveTypeNameAndMark(string typeName, bool needsAssemblyName, out TypeProxy type)
        {
            if (_reflectionAccessAnalyzer.TryResolveTypeNameAndMark(typeName, needsAssemblyName, out ITypeSymbol? foundType))
            {
                if (foundType is INamedTypeSymbol namedType && namedType.IsGenericType)
                {
                    var requireDynamicallyAccessedMembersAction = new RequireDynamicallyAccessedMembersAction(_typeNameResolver, in _diagnosticContext, _reflectionAccessAnalyzer);
                    var genericArgumentDataFlow = new GenericArgumentDataFlow(requireDynamicallyAccessedMembersAction);
                    genericArgumentDataFlow.ProcessGenericArgumentDataFlow(namedType);
                }

                type = new TypeProxy(foundType);
                return true;
            }

            type = default;
            return false;
        }

        private partial void MarkTypeForDynamicallyAccessedMembers(in TypeProxy type, DynamicallyAccessedMemberTypes dynamicallyAccessedMemberTypes) =>
            _reflectionAccessAnalyzer.GetReflectionAccessDiagnostics(type.Type, dynamicallyAccessedMemberTypes);
    }
}
