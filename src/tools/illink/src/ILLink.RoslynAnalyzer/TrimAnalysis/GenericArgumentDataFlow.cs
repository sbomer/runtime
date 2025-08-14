// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
    internal static class GenericArgumentDataFlow
    {
        public static void ProcessGenericArgumentDataFlow(TypeNameResolver typeNameResolver, Location location, INamedTypeSymbol type, Action<Diagnostic>? reportDiagnostic)
        {
            while (type is { IsGenericType: true })
            {
                ProcessGenericArgumentDataFlow(typeNameResolver, location, type.TypeArguments, type.TypeParameters, reportDiagnostic);
                type = type.ContainingType;
            }
        }

        public static void ProcessGenericArgumentDataFlow(TypeNameResolver typeNameResolver, Location location, IMethodSymbol method, Action<Diagnostic>? reportDiagnostic)
        {
            ProcessGenericArgumentDataFlow(typeNameResolver, location, method.TypeArguments, method.TypeParameters, reportDiagnostic);

            ProcessGenericArgumentDataFlow(typeNameResolver, location, method.ContainingType, reportDiagnostic);
        }

        public static void ProcessGenericArgumentDataFlow(TypeNameResolver typeNameResolver, Location location, IFieldSymbol field, Action<Diagnostic>? reportDiagnostic)
        {
            ProcessGenericArgumentDataFlow(typeNameResolver, location, field.ContainingType, reportDiagnostic);
        }

        public static void ProcessGenericArgumentDataFlow(TypeNameResolver typeNameResolver, Location location, IPropertySymbol property, Action<Diagnostic> reportDiagnostic)
        {
            ProcessGenericArgumentDataFlow(typeNameResolver, location, property.ContainingType, reportDiagnostic);
        }

        private static void ProcessGenericArgumentDataFlow(
            TypeNameResolver typeNameResolver,
            Location location,
            ImmutableArray<ITypeSymbol> typeArguments,
            ImmutableArray<ITypeParameterSymbol> typeParameters,
            Action<Diagnostic>? reportDiagnostic)
        {
            var diagnosticContext = new DiagnosticContext(location, reportDiagnostic);
            for (int i = 0; i < typeArguments.Length; i++)
            {
                var typeArgument = typeArguments[i];
                // Apply annotations to the generic argument
                var genericParameterValue = new GenericParameterValue(typeParameters[i]);
                if (genericParameterValue.DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
                {
                    SingleValue genericArgumentValue = SingleValueExtensions.FromTypeSymbol(typeArgument)!;
                    var reflectionAccessAnalyzer = new ReflectionAccessAnalyzer(reportDiagnostic, typeNameResolver, typeHierarchyType: null);
                    var requireDynamicallyAccessedMembersAction = new RequireDynamicallyAccessedMembersAction(typeNameResolver, location, reportDiagnostic, reflectionAccessAnalyzer);
                    requireDynamicallyAccessedMembersAction.Invoke(genericArgumentValue, genericParameterValue);
                }

                // Recursively process generic argument data flow on the generic argument if it itself is generic
                if (typeArgument is INamedTypeSymbol namedTypeArgument && namedTypeArgument.IsGenericType)
                    ProcessGenericArgumentDataFlow(typeNameResolver, location, namedTypeArgument, reportDiagnostic);
            }
        }

        public static bool RequiresGenericArgumentDataFlow(INamedTypeSymbol type)
        {
            System.Console.WriteLine("-- Type requires gen dataflow? " + type.ToDisplayString());
            while (type is { IsGenericType: true })
            {
                System.Console.WriteLine("was generic");
                if (RequiresGenericArgumentDataFlow(type.TypeParameters))
                {
                    System.Console.WriteLine("True 1");
                    return true;
                }

                foreach (var typeArgument in type.TypeArguments)
                {
                    if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType
                        && RequiresGenericArgumentDataFlow(namedTypeSymbol))
                    {
                        System.Console.WriteLine("True 2");
                        return true;
                    }
                }

                System.Console.WriteLine("Looking at containing type");
                type = type.ContainingType;
            }

            System.Console.WriteLine("Return false");
            return false;
        }

        public static bool RequiresGenericArgumentDataFlow(IMethodSymbol method)
        {
            System.Console.WriteLine("Requires gen dataflow? " + method.ToDisplayString());
            if (method.IsGenericMethod)
            {
                if (RequiresGenericArgumentDataFlow(method.TypeParameters))
                {
                    System.Console.WriteLine("True 1");
                    return true;
                }

                foreach (var typeArgument in method.TypeArguments)
                {
                    if (typeArgument is INamedTypeSymbol namedTypeSymbol && namedTypeSymbol.IsGenericType
                        && RequiresGenericArgumentDataFlow(namedTypeSymbol))
                    {
                        System.Console.WriteLine("True 2");
                        return true;
                    }
                }
            }

            var ret = RequiresGenericArgumentDataFlow(method.ContainingType);
            System.Console.WriteLine("Ret: " + ret);
            return ret;
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
            System.Console.WriteLine("Requires for type params...");
            foreach (var typeParameter in typeParameters)
            {
                System.Console.WriteLine("Parameter: " + typeParameter.ToDisplayString() + " owned by " + typeParameter.ContainingSymbol.ToDisplayString());
                // OH... problem is the new constraint isn't detected.
                var genericParameterValue = new GenericParameterValue(typeParameter);
                if (genericParameterValue.DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
                    return true;
            }

            return false;
        }
    }
}
