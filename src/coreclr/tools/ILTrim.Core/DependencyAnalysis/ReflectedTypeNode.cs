// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectedTypeNode : DependencyNodeCore<NodeFactory>
    {
        private readonly TypeDesc _type;

        public ReflectedTypeNode(TypeDesc type)
        {
            _type = type;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            EcmaType type = (EcmaType)_type.GetTypeDefinition();
            return new DependencyList
            {
                new DependencyListEntry(factory.TypeDefinition(type.Module, type.Handle), "Reflection-visible type metadata"),
            };
        }

        protected override string GetName(NodeFactory factory) => "Reflection-visible type: " + _type;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
