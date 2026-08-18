// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.Metadata;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents an interface that is considered used at runtime (e.g. there's a cast to it
    /// or a virtual method on it is called).
    /// </summary>
    public class InterfaceUseNode : DependencyNodeCore<NodeFactory>
    {
        private readonly EcmaType _type;

        public InterfaceUseNode(EcmaType type)
        {
            Debug.Assert(type.IsInterface);
            _type = type;
        }

        protected override string GetName(NodeFactory factory)
        {
            return $"{_type} interface used";
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            MetadataReader reader = _type.MetadataReader;
            TypeDefinition typeDefinition = reader.GetTypeDefinition(_type.Handle);

            foreach (InterfaceImplementationHandle interfaceImplementationHandle in typeDefinition.GetInterfaceImplementations())
            {
                InterfaceImplementation interfaceImplementation = reader.GetInterfaceImplementation(interfaceImplementationHandle);
                if (_type.Module.TryGetType(interfaceImplementation.Interface) is DefType baseInterface)
                {
                    yield return new(
                        factory.InterfaceUse((EcmaType)baseInterface.GetTypeDefinition()),
                        "Base interface used");
                }
            }
        }

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override bool HasConditionalStaticDependencies => false;
        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;

        internal static void AddConstrainedInterfaceMethodDependencies(
            List<CombinedDependencyListEntry> dependencies,
            NodeFactory factory,
            ConstrainedInterfaceMethodUseNode constrainedUse)
        {
            TypeDesc constrainedType = constrainedUse.ConstrainedType;
            MethodDesc interfaceMethod = constrainedUse.InterfaceMethod;
            bool isStatic = interfaceMethod.Signature.IsStatic;

            MethodDesc implementationMethod = isStatic
                ? constrainedType.ResolveInterfaceMethodToStaticVirtualMethodOnType(interfaceMethod)
                : constrainedType.ResolveInterfaceMethodToVirtualMethodOnType(interfaceMethod);

            implementationMethod ??= isStatic
                ? constrainedType.ResolveVariantInterfaceMethodToStaticVirtualMethodOnType(interfaceMethod)
                : constrainedType.ResolveVariantInterfaceMethodToVirtualMethodOnType(interfaceMethod);

            if (implementationMethod is not null)
            {
                if (!isStatic)
                {
                    implementationMethod = constrainedType.FindVirtualFunctionTargetMethodOnObjectType(implementationMethod);
                }

                AddImplementationDependencies(
                    dependencies,
                    factory,
                    constrainedUse,
                    interfaceMethod,
                    implementationMethod,
                    "Constrained interface method");
                return;
            }

            DefaultInterfaceMethodResolution resolution =
                constrainedType.ResolveInterfaceMethodToDefaultImplementationOnType(interfaceMethod, out implementationMethod);
            if (resolution == DefaultInterfaceMethodResolution.None)
            {
                resolution = constrainedType.ResolveVariantInterfaceMethodToDefaultImplementationOnType(
                    interfaceMethod,
                    out implementationMethod);
            }

            if (resolution == DefaultInterfaceMethodResolution.DefaultImplementation)
            {
                AddImplementationDependencies(
                    dependencies,
                    factory,
                    constrainedUse,
                    interfaceMethod,
                    implementationMethod,
                    "Constrained default interface method");

                dependencies.Add(new(
                    factory.InterfaceUse((EcmaType)implementationMethod.OwningType.GetTypeDefinition()),
                    constrainedUse,
                    "Interface providing constrained default implementation"));

                if (constrainedType is DefType constrainedDefType)
                {
                    AddDefaultInterfaceOverrideDependencies(
                        dependencies,
                        factory,
                        constrainedUse,
                        interfaceMethod,
                        constrainedDefType);
                }
            }
            else if (resolution is DefaultInterfaceMethodResolution.Diamond or DefaultInterfaceMethodResolution.Reabstraction &&
                constrainedType is DefType constrainedDefType)
            {
                foreach (DefType candidateInterface in constrainedDefType.RuntimeInterfaces)
                {
                    dependencies.Add(new(
                        factory.InterfaceUse((EcmaType)candidateInterface.GetTypeDefinition()),
                        constrainedUse,
                        "Interface participating in constrained default method resolution"));
                }
            }
        }

        private static void AddDefaultInterfaceOverrideDependencies(
            List<CombinedDependencyListEntry> dependencies,
            NodeFactory factory,
            ConstrainedInterfaceMethodUseNode constrainedUse,
            MethodDesc interfaceMethod,
            DefType constrainedType)
        {
            MethodDesc interfaceMethodDefinition = interfaceMethod.GetTypicalMethodDefinition();

            foreach (DefType candidateInterface in constrainedType.RuntimeInterfaces)
            {
                if (candidateInterface.GetTypeDefinition() is not EcmaType candidateDefinition ||
                    !factory.IsModuleTrimmed(candidateDefinition.Module))
                {
                    continue;
                }

                MetadataReader reader = candidateDefinition.MetadataReader;
                foreach (MethodImplementationHandle implementationHandle in
                    reader.GetTypeDefinition(candidateDefinition.Handle).GetMethodImplementations())
                {
                    MethodImplementation implementation = reader.GetMethodImplementation(implementationHandle);
                    MethodDesc declaration = candidateDefinition.Module.TryGetMethod(implementation.MethodDeclaration);
                    if (declaration?.GetTypicalMethodDefinition() == interfaceMethodDefinition)
                    {
                        dependencies.Add(new(
                            factory.MethodImplementation(candidateDefinition.Module, implementationHandle),
                            constrainedUse,
                            "Constrained default interface override"));
                    }
                }
            }
        }

        private static void AddImplementationDependencies(
            List<CombinedDependencyListEntry> dependencies,
            NodeFactory factory,
            ConstrainedInterfaceMethodUseNode constrainedUse,
            MethodDesc interfaceMethod,
            MethodDesc implementationMethod,
            string reason)
        {
            EcmaMethod implementationDefinition = (EcmaMethod)implementationMethod.GetTypicalMethodDefinition();
            dependencies.Add(new(
                factory.MethodDefinition(implementationDefinition.Module, implementationDefinition.Handle),
                constrainedUse,
                reason));

            if (implementationMethod.OwningType.GetTypeDefinition() is not EcmaType implementingType ||
                !factory.IsModuleTrimmed(implementingType.Module))
            {
                return;
            }

            MethodImplementationHandle implementationHandle =
                MethodImplementationNode.TryGetMethodImplementationHandle(
                    implementingType,
                    interfaceMethod,
                    implementationMethod);
            if (!implementationHandle.IsNil)
            {
                dependencies.Add(new(
                    factory.MethodImplementation(implementingType.Module, implementationHandle),
                    constrainedUse,
                    "Constrained interface MethodImpl"));
            }
        }
    }
}
