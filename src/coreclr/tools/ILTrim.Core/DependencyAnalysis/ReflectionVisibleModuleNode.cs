// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.Metadata;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectionVisibleModuleNode : DependencyNodeCore<NodeFactory>
    {
        private readonly EcmaModule _module;

        public ReflectionVisibleModuleNode(EcmaModule module)
        {
            _module = module;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            var dependencies = new DependencyList();
            foreach (ExportedTypeHandle exportedType in _module.MetadataReader.ExportedTypes)
                dependencies.Add(factory.ExportedType(_module, exportedType), "Reflection-visible type forwarder");

            return dependencies;
        }

        protected override string GetName(NodeFactory factory)
            => "Reflection-visible module metadata: " + _module;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
