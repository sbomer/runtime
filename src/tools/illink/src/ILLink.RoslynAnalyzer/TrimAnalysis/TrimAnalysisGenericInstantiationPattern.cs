// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using ILLink.Shared.TrimAnalysis;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
	public readonly record struct TrimAnalysisGenericInstantiationPattern
	{
		public ISymbol GenericInstantiation { init; get; }
		public IOperation Operation { init; get; }
		public ISymbol OwningSymbol { init; get; }

		public TrimAnalysisGenericInstantiationPattern (
			ISymbol genericInstantiation,
			IOperation operation,
			ISymbol owningSymbol)
		{
			GenericInstantiation = genericInstantiation;
			Operation = operation;
			OwningSymbol = owningSymbol;
		}

		// No Merge - there's nothing to merge since this pattern is uniquely identified by both the origin and the entity
		// and there's only one way to access the referenced method.

		public IEnumerable<Diagnostic> CollectDiagnostics (DataFlowAnalyzerContext context)
		{
			DiagnosticContext diagnosticContext = new (Operation.Syntax.GetLocation ());
			if (context.EnableTrimAnalyzer && !OwningSymbol.IsInRequiresUnreferencedCodeAttributeScope (out _)) {
				GenericArgumentDataFlow.ProcessGenericArgumentDataFlow (diagnosticContext, GenericInstantiation, Operation.Syntax.GetLocation ());
			}

			return diagnosticContext.Diagnostics;
		}
	}
}
