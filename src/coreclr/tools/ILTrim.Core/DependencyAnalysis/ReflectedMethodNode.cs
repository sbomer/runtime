// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectedMethodNode : DependencyNodeCore<NodeFactory>
    {
        private readonly MethodDesc _method;

        public ReflectedMethodNode(MethodDesc method)
        {
            _method = method;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            EcmaMethod method = (EcmaMethod)_method.GetTypicalMethodDefinition();
            return new DependencyList
            {
                new DependencyListEntry(factory.MethodDefinition(method.Module, method.Handle), "Reflection-visible method metadata"),
                new DependencyListEntry(factory.ReflectedType(method.OwningType), "Reflection-visible declaring type"),
            };
        }

        protected override string GetName(NodeFactory factory) => "Reflection-visible method: " + _method;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
