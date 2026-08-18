// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using Internal.IL;
using Internal.TypeSystem;

using ILCompiler.DependencyAnalysisFramework;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Makes the generic context represented by a method token available to dynamic dependency analysis.
    /// </summary>
    public sealed class MethodInstantiationNode : DependencyNodeCore<NodeFactory>
    {
        public MethodInstantiationNode(MethodDesc method)
        {
            Method = method;
        }

        public MethodDesc Method { get; }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            MethodIL methodIL = factory.FlowAnnotations.ILProvider.GetMethodIL(Method);
            if (methodIL is null)
                return null;

            DependencyList dependencies = null;
            TypeDesc constrainedType = null;
            ILReader reader = new(methodIL.GetILBytes());

            while (reader.HasNext)
            {
                ILOpcode opcode = reader.ReadILOpcode();
                switch (opcode)
                {
                    case ILOpcode.constrained:
                        constrainedType = methodIL.GetObject(reader.ReadILToken()) as TypeDesc;
                        break;

                    case ILOpcode.call:
                    case ILOpcode.callvirt:
                    case ILOpcode.ldftn:
                    case ILOpcode.ldvirtftn:
                        MethodDesc calledMethod = methodIL.GetObject(reader.ReadILToken()) as MethodDesc;
                        if (constrainedType is not null &&
                            calledMethod?.OwningType.IsInterface == true &&
                            !constrainedType.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true) &&
                            !calledMethod.OwningType.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true) &&
                            !MethodInstantiationContainsSignatureVariables(calledMethod) &&
                            calledMethod.IsVirtual)
                        {
                            dependencies ??= new DependencyList();
                            dependencies.Add(
                                factory.ConstrainedInterfaceMethodUse(constrainedType, calledMethod, GetCallKind(opcode)),
                                "Constrained interface method use");
                        }

                        constrainedType = null;
                        break;

                    case ILOpcode.no:
                    case ILOpcode.readonly_:
                    case ILOpcode.tail:
                    case ILOpcode.unaligned:
                    case ILOpcode.volatile_:
                        reader.Skip(opcode);
                        break;

                    default:
                        constrainedType = null;
                        reader.Skip(opcode);
                        break;
                }
            }

            return dependencies;

            static ConstrainedInterfaceMethodUseKind GetCallKind(ILOpcode opcode) => opcode switch
            {
                ILOpcode.call => ConstrainedInterfaceMethodUseKind.Call,
                ILOpcode.callvirt => ConstrainedInterfaceMethodUseKind.CallVirt,
                ILOpcode.ldftn => ConstrainedInterfaceMethodUseKind.LoadFunction,
                ILOpcode.ldvirtftn => ConstrainedInterfaceMethodUseKind.LoadVirtualFunction,
                _ => throw new System.Diagnostics.UnreachableException(),
            };

            static bool MethodInstantiationContainsSignatureVariables(MethodDesc method)
            {
                foreach (TypeDesc argument in method.Instantiation)
                {
                    if (argument.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true))
                        return true;
                }

                return false;
            }
        }

        protected override string GetName(NodeFactory factory)
        {
            return "Method instantiation for " + Method;
        }

        public override bool InterestingForDynamicDependencyAnalysis => true;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory context) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
