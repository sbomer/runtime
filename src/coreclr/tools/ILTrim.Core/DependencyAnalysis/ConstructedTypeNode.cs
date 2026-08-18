// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a type that is considered allocated at runtime (e.g. with a "new").
    /// </summary>
    public class ConstructedTypeNode : DependencyNodeCore<NodeFactory>, INodeWithDeferredDependencies
    {
        private readonly EcmaType _type;
        private IReadOnlyCollection<CombinedDependencyListEntry> _conditionalDependencies;

        public ConstructedTypeNode(EcmaType type)
        {
            _type = type;
        }

        public override bool HasConditionalStaticDependencies => _conditionalDependencies.Count > 0;

        void INodeWithDeferredDependencies.ComputeDependencies(NodeFactory factory)
        {
            List<CombinedDependencyListEntry> result = null;

            if (factory.IsModuleTrimmed(_type.Module))
            {
                // Quickly check if going over the virtual slots is worth it for this type.
                bool hasVirtualMethods = false;
                foreach (MethodDesc method in _type.GetAllVirtualMethods())
                {
                    hasVirtualMethods = true;
                    break;
                }

                if (hasVirtualMethods)
                {
                    // For each virtual method slot (e.g. Object.GetHashCode()), check whether the current type
                    // provides an implementation of the virtual method (e.g. SomeFoo.GetHashCode()),
                    // if so, make sure we generate the body.
                    foreach (MethodDesc decl in _type.EnumAllVirtualSlots())
                    {
                        MethodDesc impl = _type.FindVirtualFunctionTargetMethodOnObjectType(decl);

                        // We're only interested in the case when it's implemented on this type.
                        // If the implementation comes from a base type, that's covered by the base type
                        // ConstructedTypeNode.
                        if (impl.OwningType == _type)
                        {
                            // If the slot defining virtual method is used, make sure we generate the implementation method.
                            var ecmaImpl = (EcmaMethod)impl.GetTypicalMethodDefinition();

                            EcmaMethod declDefinition = (EcmaMethod)decl.GetTypicalMethodDefinition();
                            VirtualMethodUseNode declUse = factory.VirtualMethodUse(declDefinition);

                            result ??= new List<CombinedDependencyListEntry>();
                            result.Add(new(
                                factory.MethodDefinition(ecmaImpl.Module, ecmaImpl.Handle),
                                declUse,
                                "OverrideOnInstantiatedType"));

                            var implHandle = MethodImplementationNode.TryGetMethodImplementationHandle(_type, declDefinition, impl);
                            if (!implHandle.IsNil)
                            {
                                result.Add(new(
                                    factory.MethodImplementation(_type.Module, implHandle),
                                    declUse,
                                    "Explicitly implemented virtual method"));
                            }
                        }
                    }
                }
            }

            // For each interface, figure out what implements the individual interface methods on it.
            foreach (DefType intface in _type.RuntimeInterfaces)
            {
                foreach (MethodDesc interfaceMethod in intface.EnumAllVirtualSlots())
                {
                    MethodDesc slotMethod = MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(interfaceMethod);
                    var slotDefinition = (EcmaMethod)slotMethod.GetTypicalMethodDefinition();
                    VirtualMethodUseNode interfaceMethodUse = factory.VirtualMethodUse(slotDefinition);

                    MethodDesc implMethod = interfaceMethod.Signature.IsStatic
                        ? _type.ResolveInterfaceMethodToStaticVirtualMethodOnType(interfaceMethod)
                        : _type.ResolveInterfaceMethodToVirtualMethodOnType(interfaceMethod);
                    if (implMethod != null)
                    {
                        result ??= new List<CombinedDependencyListEntry>();

                        // Interface method implementation provided within the class hierarchy.
                        EcmaMethod implementationDefinition = (EcmaMethod)implMethod.GetTypicalMethodDefinition();
                        if (interfaceMethod.Signature.IsStatic)
                        {
                            result.Add(new(factory.MethodDefinition(implementationDefinition.Module, implementationDefinition.Handle),
                                interfaceMethodUse,
                                "Static interface method"));
                        }
                        else
                        {
                            result.Add(new(factory.VirtualMethodUse(implementationDefinition),
                                interfaceMethodUse,
                                "Interface method"));
                        }

                        if (implMethod.OwningType.GetTypeDefinition() is EcmaType implementingType &&
                            factory.IsModuleTrimmed(implementingType.Module))
                        {
                            MethodImplementationHandle implHandle = MethodImplementationNode.TryGetMethodImplementationHandle(
                                implementingType,
                                interfaceMethod,
                                implMethod);
                            if (!implHandle.IsNil)
                            {
                                result.Add(new(factory.MethodImplementation(implementingType.Module, implHandle),
                                    interfaceMethodUse,
                                    "Explicitly implemented interface method"));
                            }
                        }
                    }
                    else
                    {
                        // Is the implementation provided by a default interface method?
                        var resolution = _type.ResolveInterfaceMethodToDefaultImplementationOnType(interfaceMethod, out implMethod);
                        if (resolution == DefaultInterfaceMethodResolution.DefaultImplementation)
                        {
                            result ??= new List<CombinedDependencyListEntry>();
                            EcmaMethod implementationDefinition = (EcmaMethod)implMethod.GetTypicalMethodDefinition();
                            result.Add(new(factory.MethodDefinition(implementationDefinition.Module, implementationDefinition.Handle),
                                interfaceMethodUse,
                                "Default interface method"));

                            EcmaType providingInterface = (EcmaType)implMethod.OwningType.GetTypeDefinition();
                            result.Add(new(
                                factory.InterfaceUse(providingInterface),
                                interfaceMethodUse,
                                "Interface providing default implementation"));

                            MethodImplementationHandle implHandle = MethodImplementationNode.TryGetMethodImplementationHandle(
                                providingInterface,
                                interfaceMethod,
                                implMethod);
                            if (!implHandle.IsNil)
                            {
                                result.Add(new(
                                    factory.MethodImplementation(providingInterface.Module, implHandle),
                                    interfaceMethodUse,
                                    "Explicit default interface method"));
                            }
                        }
                        else if (resolution is DefaultInterfaceMethodResolution.Diamond or DefaultInterfaceMethodResolution.Reabstraction)
                        {
                            result ??= new List<CombinedDependencyListEntry>();
                            foreach (DefType candidateInterface in _type.RuntimeInterfaces)
                            {
                                result.Add(new(
                                    factory.InterfaceUse((EcmaType)candidateInterface.GetTypeDefinition()),
                                    interfaceMethodUse,
                                    "Interface participating in default method resolution"));
                            }
                        }
                    }
                }
            }

            // For each interface, make the interface considered constructed if the interface is used
            IReadOnlyList<DefType> directlyImplementedInterfaces = GetDirectlyImplementedInterfaces(_type);
            foreach (DefType intface in _type.RuntimeInterfaces)
            {
                result ??= new List<CombinedDependencyListEntry>();
                EcmaType interfaceDefinition = (EcmaType)intface.GetTypeDefinition();
                result.Add(new(factory.ConstructedType(interfaceDefinition),
                    factory.InterfaceUse(interfaceDefinition),
                    "Used interface on a constructed type"));

                bool directlyImplemented = false;
                foreach (DefType directlyImplementedInterface in directlyImplementedInterfaces)
                {
                    if (directlyImplementedInterface == intface)
                    {
                        directlyImplemented = true;
                        break;
                    }
                }

                if (!directlyImplemented)
                {
                    foreach (DefType directlyImplementedInterface in directlyImplementedInterfaces)
                    {
                        if (ImplementsInterface(directlyImplementedInterface, intface))
                        {
                            result.Add(new(
                                factory.InterfaceUse((EcmaType)directlyImplementedInterface.GetTypeDefinition()),
                                factory.InterfaceUse(interfaceDefinition),
                                "Direct interface implementing a used interface"));
                            break;
                        }
                    }
                }
            }

            // Check to see if we have any dataflow annotations on the type.
            // The check below also covers flow annotations inherited through base classes and implemented interfaces.
            bool allocatedWithFlowAnnotations = factory.FlowAnnotations.GetTypeAnnotation(_type) != default;

            if (allocatedWithFlowAnnotations)
            {
                result ??= new List<CombinedDependencyListEntry>();
                result.Add(new DependencyNodeCore<NodeFactory>.CombinedDependencyListEntry(
                    factory.ObjectGetTypeFlowDependencies(_type),
                    factory.ObjectGetTypeCalled(_type),
                    "Type exists and GetType called on it"));
            }

            if (allocatedWithFlowAnnotations
                && !_type.IsInterface /* "IFoo x; x.GetType();" -> this doesn't actually return an interface type */)
            {
                // We have some flow annotations on this type.
                //
                // The flow annotations are supposed to ensure that should we call object.GetType on a location
                // typed as one of the annotated subclasses of this type, this type is going to have the specified
                // members kept. We don't keep them right away, but condition them on the object.GetType being called.
                //
                // Now we figure out where the annotations are coming from:

                DefType baseType = _type.BaseType;
                if (baseType != null && factory.FlowAnnotations.GetTypeAnnotation(baseType) != default)
                {
                    // There's an annotation on the base type. If object.GetType was called on something
                    // statically typed as the base type, we might actually be calling it on this type.
                    // Ensure we have the flow dependencies.
                    result ??= new List<CombinedDependencyListEntry>();
                    result.Add(new(
                        factory.ObjectGetTypeCalled(_type),
                        factory.ObjectGetTypeCalled((EcmaType)baseType.GetTypeDefinition()),
                        "GetType called on the base type"));

                    // We don't have to follow all the bases since the base MethodTable will bubble this up
                }

                foreach (DefType interfaceType in _type.RuntimeInterfaces)
                {
                    if (factory.FlowAnnotations.GetTypeAnnotation(interfaceType) != default)
                    {
                        // There's an annotation on the interface type. If object.GetType was called on something
                        // statically typed as the interface type, we might actually be calling it on this type.
                        // Ensure we have the flow dependencies.
                        result ??= new List<CombinedDependencyListEntry>();
                        result.Add(new(
                            factory.ObjectGetTypeCalled(_type),
                            factory.ObjectGetTypeCalled((EcmaType)interfaceType.GetTypeDefinition()),
                            "GetType called on the interface"));
                    }

                    // We don't have to recurse into the interface because we're inspecting runtime interfaces
                    // and this list is already flattened.
                }

                // Note we don't add any conditional dependencies if this type itself was annotated and none
                // of the bases/interfaces are annotated.
                // ObjectGetTypeFlowDependencies don't need to be conditional in that case. They'll be added as needed.
            }

            _conditionalDependencies = result ?? (IReadOnlyCollection<CombinedDependencyListEntry>)Array.Empty<CombinedDependencyListEntry>();
        }

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
        {
            System.Diagnostics.Debug.Assert(_conditionalDependencies != null);
            return _conditionalDependencies;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            // Call GetTypeDefinition in case the base is an instantiated generic type.
            TypeDesc baseType = _type.BaseType?.GetTypeDefinition();
            if (baseType != null)
            {
                yield return new(factory.ConstructedType((EcmaType)baseType), "Base type");
            }
        }

        protected override string GetName(NodeFactory factory)
        {
            return $"{_type} constructed";
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool StaticDependenciesAreComputed => _conditionalDependencies != null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;

        private static IReadOnlyList<DefType> GetDirectlyImplementedInterfaces(EcmaType type)
        {
            MetadataReader reader = type.MetadataReader;
            var result = new List<DefType>();

            foreach (InterfaceImplementationHandle interfaceImplementationHandle in reader.GetTypeDefinition(type.Handle).GetInterfaceImplementations())
            {
                InterfaceImplementation interfaceImplementation = reader.GetInterfaceImplementation(interfaceImplementationHandle);
                if (type.Module.TryGetType(interfaceImplementation.Interface) is DefType interfaceType)
                    result.Add(interfaceType);
            }

            return result;
        }

        private static bool ImplementsInterface(DefType interfaceType, DefType baseInterface)
        {
            foreach (DefType runtimeInterface in interfaceType.RuntimeInterfaces)
            {
                if (runtimeInterface == baseInterface)
                    return true;
            }

            return false;
        }

    }
}
