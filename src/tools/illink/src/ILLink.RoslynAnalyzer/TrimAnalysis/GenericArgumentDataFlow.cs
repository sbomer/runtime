// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using ILLink.RoslynAnalyzer.DataFlow;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;
using ILLink.Shared.TypeSystemProxy;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
    internal sealed class GenericArgumentDataFlow
    {
        readonly RequireDynamicallyAccessedMembersAction _requireDynamicallyAccessedMembersAction;

        public GenericArgumentDataFlow(RequireDynamicallyAccessedMembersAction requireDynamicallyAccessedMembersAction)
        {
            _requireDynamicallyAccessedMembersAction = requireDynamicallyAccessedMembersAction;
        }

        public void ProcessGenericArgumentDataFlow(INamedTypeSymbol type)
        {
            while (type is { IsGenericType: true })
            {
                ProcessGenericArgumentDataFlow(type.TypeArguments, type.TypeParameters);
                type = type.ContainingType;
            }
        }

        public void ProcessGenericArgumentDataFlow(IMethodSymbol method)
        {
            ProcessGenericArgumentDataFlow(method.TypeArguments, method.TypeParameters);

            ProcessGenericArgumentDataFlow(method.ContainingType);
        }

        public void ProcessGenericArgumentDataFlow(IFieldSymbol field)
        {
            ProcessGenericArgumentDataFlow(field.ContainingType);
        }

        public void ProcessGenericArgumentDataFlow(IPropertySymbol property)
        {
            ProcessGenericArgumentDataFlow(property.ContainingType);
        }

        private void ProcessGenericArgumentDataFlow(ImmutableArray<ITypeSymbol> typeArguments, ImmutableArray<ITypeParameterSymbol> typeParameters)
        {
            for (int i = 0; i < typeArguments.Length; i++)
            {
                var typeArgument = typeArguments[i];
                // Apply annotations to the generic argument
                var genericParameterValue = FlowAnnotations.Instance.GetGenericParameterValue(new GenericParameterProxy(typeParameters[i]));
                if (genericParameterValue.DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
                {
                    SingleValue genericArgumentValue = SingleValueExtensions.FromTypeSymbol(typeArgument)!;
                    _requireDynamicallyAccessedMembersAction.Invoke(genericArgumentValue, genericParameterValue);
                }

                // Recursively process generic argument data flow on the generic argument if it itself is generic
                if (typeArgument is INamedTypeSymbol namedTypeArgument && namedTypeArgument.IsGenericType)
                    ProcessGenericArgumentDataFlow(namedTypeArgument);
            }
        }

        public static bool RequiresGenericArgumentDataFlow(INamedTypeSymbol type)
        {
            while (type is { IsGenericType: true })
            {
                if (RequiresGenericArgumentDataFlow(type.TypeParameters))
                    return true;

                foreach (var typeArgument in type.TypeArguments)
                {
                    if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType
                        && RequiresGenericArgumentDataFlow(namedTypeSymbol))
                        return true;
                }

                type = type.ContainingType;
            }

            return false;
        }

        public static bool RequiresGenericArgumentDataFlow(IMethodSymbol method)
        {
            if (method.IsGenericMethod)
            {
                if (RequiresGenericArgumentDataFlow(method.TypeParameters))
                    return true;

                foreach (var typeArgument in method.TypeArguments)
                {
                    if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType
                        && RequiresGenericArgumentDataFlow(namedTypeSymbol))
                        return true;
                }
            }

            return RequiresGenericArgumentDataFlow(method.ContainingType);
        }

        public static bool RequiresGenericArgumentDataFlow(IFieldSymbol field)
        {
            return RequiresGenericArgumentDataFlow(field.ContainingType);
        }

        public static bool RequiresGenericArgumentDataFlow(IPropertySymbol property)
        {
            return RequiresGenericArgumentDataFlow(property.ContainingType);
        }

        private static bool RequiresGenericArgumentDataFlow(ImmutableArray<ITypeParameterSymbol> typeParameters)
        {
            foreach (var typeParameter in typeParameters)
            {
                var genericParameterValue = FlowAnnotations.Instance.GetGenericParameterValue(new GenericParameterProxy(typeParameter));
                if (genericParameterValue.DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
                    return true;
            }

            return false;
        }
    }
}
