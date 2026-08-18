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
    public readonly struct InterfaceImplementationUseKey : IEquatable<InterfaceImplementationUseKey>
    {
        public InterfaceImplementationUseKey(EcmaType implementingType, EcmaType interfaceType)
        {
            ImplementingType = implementingType;
            InterfaceType = interfaceType;
        }

        public EcmaType ImplementingType { get; }
        public EcmaType InterfaceType { get; }

        public bool Equals(InterfaceImplementationUseKey other)
        {
            return ImplementingType == other.ImplementingType && InterfaceType == other.InterfaceType;
        }

        public override bool Equals(object obj)
        {
            return obj is InterfaceImplementationUseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ImplementingType, InterfaceType);
        }
    }

    /// <summary>
    /// Represents an interface implementation needed on one specific type.
    /// </summary>
    public sealed class InterfaceImplementationUseNode : DependencyNodeCore<NodeFactory>
    {
        public InterfaceImplementationUseNode(InterfaceImplementationUseKey key)
        {
            ImplementingType = key.ImplementingType;
            InterfaceType = key.InterfaceType;
        }

        public EcmaType ImplementingType { get; }
        public EcmaType InterfaceType { get; }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            MetadataReader reader = InterfaceType.MetadataReader;
            foreach (InterfaceImplementationHandle interfaceImplementationHandle in
                reader.GetTypeDefinition(InterfaceType.Handle).GetInterfaceImplementations())
            {
                InterfaceImplementation interfaceImplementation = reader.GetInterfaceImplementation(interfaceImplementationHandle);
                if (InterfaceType.Module.TryGetType(interfaceImplementation.Interface) is DefType baseInterface)
                {
                    yield return new(
                        factory.InterfaceImplementationUse(
                            InterfaceType,
                            (EcmaType)baseInterface.GetTypeDefinition()),
                        "Base interface implementation");
                }
            }
        }

        protected override string GetName(NodeFactory factory)
        {
            return $"{ImplementingType} implements {InterfaceType}";
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
