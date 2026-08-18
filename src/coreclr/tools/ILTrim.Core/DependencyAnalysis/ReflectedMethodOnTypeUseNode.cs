// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectedMethodOnTypeUseNode : DependencyNodeCore<NodeFactory>
    {
        private readonly MethodDesc _method;

        public ReflectedMethodOnTypeUseNode(MethodDesc method)
        {
            _method = method;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory) => null;

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
        {
            EcmaMethod method = (EcmaMethod)_method.GetTypicalMethodDefinition();
            EcmaType owningType = (EcmaType)method.OwningType.GetTypeDefinition();
            yield return new CombinedDependencyListEntry(
                factory.ReflectedMethod(method),
                factory.TypeDefinition(owningType.Module, owningType.Handle),
                "Descriptor-preserved method on used type");
        }

        protected override string GetName(NodeFactory factory)
            => "Conditionally reflection-visible method: " + _method;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => true;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
