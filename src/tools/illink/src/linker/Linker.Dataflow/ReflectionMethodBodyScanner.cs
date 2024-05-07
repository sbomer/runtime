// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.


using ILLink.Shared.DataFlow;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Linker.DataFlow;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;

namespace Mono.Linker.Dataflow
{
	sealed class ReflectionMethodBodyScanner : MethodBodyScanner
	{
		public ReflectionMethodBodyScanner (LinkContext context, ReflectionHandler handler)
			: base (context, handler)
		{
		}

		public override void Scan (MethodIL methodIL, ref InterproceduralState interproceduralState)
		{
			base.Scan (methodIL, ref interproceduralState);
			_handler.HandleReturnValue (methodIL.Method, ReturnValue);
		}
	}

	sealed class ReflectionScanner : LocalDataFlowScanner
	{
		public ReflectionScanner (LinkContext context, ReflectionHandler handler, InterproceduralState interproceduralState)
			: base (context, handler, interproceduralState)
		{
		}

		public override void Scan (BasicBlock block, BasicBlockDataFlowState<MultiValue, FeatureContext, ValueSetLattice<SingleValue>, FeatureContextLattice> state)
		{
			base.Scan (block, state);
		}

		public void HandleReturnValue (MethodDefinition method)
		{
			_handler.HandleReturnValue (method, ReturnValue);
		}
	}
}
