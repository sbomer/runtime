// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Internal.IL;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.Dataflow;
using ILCompiler.DependencyAnalysisFramework;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Performs dataflow analysis for a method and its compiler-generated callees.
    /// </summary>
    public sealed class DataflowAnalyzedMethodNode : DependencyNodeCore<NodeFactory>
    {
        private readonly EcmaMethod _method;
        private List<(MethodDesc OwningMethod, INodeWithRuntimeDeterminedDependencies Dependency)> _runtimeDependencies = new();

        public DataflowAnalyzedMethodNode(EcmaMethod method)
        {
            Debug.Assert(method.IsTypicalMethodDefinition);
            Debug.Assert(!CompilerGeneratedState.IsNestedFunctionOrStateMachineMember(method));
            _method = method;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            try
            {
                Mono.Linker.AssemblyAction action = factory.Settings.CalculateAssemblyAction(_method.Module);
                MethodIL methodIL = action is Mono.Linker.AssemblyAction.Copy or Mono.Linker.AssemblyAction.CopyUsed
                    ? EcmaMethodIL.Create(_method)
                    : factory.FlowAnnotations.ILProvider.GetMethodIL(_method);
                return ReflectionMethodBodyScanner.ScanAndProcessReturnValue(
                    factory, factory.FlowAnnotations, factory.Logger, methodIL, out _runtimeDependencies);
            }
            catch (TypeSystemException)
            {
                _runtimeDependencies = new();
                return Array.Empty<DependencyListEntry>();
            }
        }

        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(
            List<DependencyNodeCore<NodeFactory>> markedNodes,
            int firstNode,
            NodeFactory factory)
        {
            for (int i = firstNode; i < markedNodes.Count; i++)
            {
                if (markedNodes[i] is not MethodInstantiationNode methodInstantiation)
                    continue;

                MethodDesc method = methodInstantiation.Method;
                MethodDesc typicalMethod = method.GetTypicalMethodDefinition();

                foreach ((MethodDesc owningMethod, INodeWithRuntimeDeterminedDependencies dependency) in _runtimeDependencies)
                {
                    if (owningMethod != typicalMethod)
                        continue;

                    foreach (DependencyListEntry instantiatedDependency in dependency.InstantiateDependencies(
                        factory,
                        method.OwningType.Instantiation,
                        method.Instantiation,
                        isConcreteInstantiation: !method.IsSharedByGenericInstantiations))
                    {
                        yield return new CombinedDependencyListEntry(
                            instantiatedDependency.Node,
                            null,
                            instantiatedDependency.Reason);
                    }
                }
            }
        }

        protected override string GetName(NodeFactory factory)
        {
            return "Dataflow analysis for " + _method;
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => _runtimeDependencies.Count > 0;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory context) => null;
    }
}
