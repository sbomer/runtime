// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectedFieldNode : DependencyNodeCore<NodeFactory>
    {
        private readonly FieldDesc _field;

        public ReflectedFieldNode(FieldDesc field)
        {
            _field = field;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            EcmaField field = (EcmaField)_field.GetTypicalFieldDefinition();
            return new DependencyList
            {
                new DependencyListEntry(factory.FieldDefinition(field.Module, field.Handle), "Reflection-visible field metadata"),
                new DependencyListEntry(factory.ReflectedType(field.OwningType), "Reflection-visible declaring type"),
            };
        }

        protected override string GetName(NodeFactory factory) => "Reflection-visible field: " + _field;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
