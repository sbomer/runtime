// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Resolves exact constrained dispatch facts for a single interface.
    /// </summary>
    public sealed class ConstrainedInterfaceMethodUseResolverNode : DependencyNodeCore<NodeFactory>
    {
        private readonly EcmaType _interfaceType;

        public ConstrainedInterfaceMethodUseResolverNode(EcmaType interfaceType)
        {
            _interfaceType = interfaceType;
        }

        protected override string GetName(NodeFactory factory)
        {
            return $"Constrained interface dispatch resolver: {_interfaceType}";
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => true;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;

        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(
            List<DependencyNodeCore<NodeFactory>> markedNodes,
            int firstNode,
            NodeFactory factory)
        {
            var dependencies = new List<CombinedDependencyListEntry>();

            for (int i = firstNode; i < markedNodes.Count; i++)
            {
                if (markedNodes[i] is ConstrainedInterfaceMethodUseNode constrainedUse &&
                    constrainedUse.InterfaceMethod.OwningType.GetTypeDefinition() == _interfaceType)
                {
                    InterfaceUseNode.AddConstrainedInterfaceMethodDependencies(
                        dependencies,
                        factory,
                        constrainedUse);
                }
            }

            return dependencies;
        }
    }
}
