// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ILCompiler.DependencyAnalysis;
using Internal.TypeSystem;

using DependencyList = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyList;

#nullable enable

namespace ILCompiler
{
    // Stub for RootingHelpers — the shared dataflow code calls these to record
    // that a type/method/field was accessed via reflection.
    public static class RootingHelpers
    {
        public static bool TryGetDependenciesForReflectedType(
            ref DependencyList dependencies, NodeFactory factory, TypeDesc type, string reason)
        {
            try
            {
                if (type.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true))
                    type = type.GetTypeDefinition();

                dependencies ??= new DependencyList();
                dependencies.Add(factory.ReflectedType(type), reason);
                return true;
            }
            catch (TypeSystemException)
            {
                return false;
            }
        }

        public static bool TryGetDependenciesForReflectedMethod(
            ref DependencyList dependencies, NodeFactory factory, MethodDesc method, string reason)
        {
            try
            {
                dependencies ??= new DependencyList();
                dependencies.Add(factory.ReflectedMethod(method.GetTypicalMethodDefinition()), reason);
                return true;
            }
            catch (TypeSystemException)
            {
                return false;
            }
        }

        public static bool TryGetDependenciesForReflectedField(
            ref DependencyList dependencies, NodeFactory factory, FieldDesc field, string reason)
        {
            try
            {
                dependencies ??= new DependencyList();
                dependencies.Add(factory.ReflectedField(field.GetTypicalFieldDefinition()), reason);
                return true;
            }
            catch (TypeSystemException)
            {
                return false;
            }
        }
    }
}
