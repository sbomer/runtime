// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

using Internal.TypeSystem;

using ILCompiler.DependencyAnalysisFramework;

namespace ILCompiler.DependencyAnalysis
{
    public enum ConstrainedInterfaceMethodUseKind
    {
        Call,
        CallVirt,
        LoadFunction,
        LoadVirtualFunction,
    }

    public readonly struct ConstrainedInterfaceMethodUseKey : IEquatable<ConstrainedInterfaceMethodUseKey>
    {
        public ConstrainedInterfaceMethodUseKey(
            TypeDesc constrainedType,
            MethodDesc interfaceMethod,
            ConstrainedInterfaceMethodUseKind kind)
        {
            ConstrainedType = constrainedType;
            InterfaceMethod = interfaceMethod;
            Kind = kind;
        }

        public TypeDesc ConstrainedType { get; }
        public MethodDesc InterfaceMethod { get; }
        public ConstrainedInterfaceMethodUseKind Kind { get; }

        public bool Equals(ConstrainedInterfaceMethodUseKey other)
        {
            return ConstrainedType == other.ConstrainedType &&
                InterfaceMethod == other.InterfaceMethod &&
                Kind == other.Kind;
        }

        public override bool Equals(object obj)
        {
            return obj is ConstrainedInterfaceMethodUseKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(ConstrainedType, InterfaceMethod, Kind);
        }
    }

    /// <summary>
    /// Records an exact constrained interface dispatch discovered in instantiated IL.
    /// </summary>
    public sealed class ConstrainedInterfaceMethodUseNode : DependencyNodeCore<NodeFactory>
    {
        public ConstrainedInterfaceMethodUseNode(ConstrainedInterfaceMethodUseKey key)
        {
            ConstrainedType = key.ConstrainedType;
            InterfaceMethod = key.InterfaceMethod;
            Kind = key.Kind;
        }

        public TypeDesc ConstrainedType { get; }
        public MethodDesc InterfaceMethod { get; }
        public ConstrainedInterfaceMethodUseKind Kind { get; }

        protected override string GetName(NodeFactory factory)
        {
            return $"{Kind} constrained interface method use: {ConstrainedType} -> {InterfaceMethod}";
        }

        public override bool InterestingForDynamicDependencyAnalysis => true;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory context) => null;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory context) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
