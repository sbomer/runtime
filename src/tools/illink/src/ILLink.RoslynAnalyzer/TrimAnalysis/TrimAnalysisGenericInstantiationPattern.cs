// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using ILLink.RoslynAnalyzer.DataFlow;
using ILLink.Shared.TrimAnalysis;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
    internal readonly record struct TrimAnalysisGenericInstantiationPattern
    {
        public ISymbol GenericInstantiation { get; init; }
        public IOperation Operation { get; init; }
        public ISymbol OwningSymbol { get; init; }
        public FeatureContext FeatureContext { get; init; }

        public TrimAnalysisGenericInstantiationPattern(
            ISymbol genericInstantiation,
            IOperation operation,
            ISymbol owningSymbol,
            FeatureContext featureContext)
        {
            GenericInstantiation = genericInstantiation;
            Operation = operation;
            OwningSymbol = owningSymbol;
            FeatureContext = featureContext.DeepCopy();
        }

        public TrimAnalysisGenericInstantiationPattern Merge(
            FeatureContextLattice featureContextLattice,
            TrimAnalysisGenericInstantiationPattern other)
        {
            Debug.Assert(Operation == other.Operation);
            Debug.Assert(SymbolEqualityComparer.Default.Equals(GenericInstantiation, other.GenericInstantiation));
            Debug.Assert(SymbolEqualityComparer.Default.Equals(OwningSymbol, other.OwningSymbol));

            return new TrimAnalysisGenericInstantiationPattern(
                GenericInstantiation,
                Operation,
                OwningSymbol,
                featureContextLattice.Meet(FeatureContext, other.FeatureContext));
        }

        public void ReportDiagnostics(DataFlowAnalyzerContext context, Action<Diagnostic> reportDiagnostic)
        {
            // Need to keep the EnableTrimAnalyzer check.
            // Because we only added the inner IsEnabled check to the reflection-acccess path.
            // But the generic warnings include DAM mismatch of type param, not just reflection access.
            // Q: then should we also keep the IsInRequires check? Or does the DAM mismatch check that already?
            // Same with feature check... indeed, that one isn't done broadly enough.
            // Do we really want reflection diagnostics to show for all of the analyzers?
            // Naot is lucky because it can assume trim analyzer enabled, and doesn't care about feature checks.
            // Analyzer needs to do what:
            // If we show reflection diags for all analyzers,
            // - should we then _not_ show mismatch warning for all analyzers?
            // correct.
            // If only AOT analyzer is enabled?
            // If only single-file analyzer is enabled? Should warn on reflection to RequiresAssemblyFiles?
            // Probably _should_. Will be incomplete. But doesn't hurt.
            // Why'd I turn those on? Because of parity with ILC. Let's not do that here.
            // Could do it in a separate change if desired.
            if (context.EnableTrimAnalyzer &&
                !OwningSymbol.IsInRequiresUnreferencedCodeAttributeScope(out _))
            {
                var location = Operation.Syntax.GetLocation();
                var typeNameResolver = new TypeNameResolver(context.Compilation);
                var diagnosticContext = new DiagnosticContext(location, OwningSymbol, reportDiagnostic, context, FeatureContext);
                var reflectionAccessAnalyzer = new ReflectionAccessAnalyzer(in diagnosticContext, typeNameResolver, typeHierarchyType: null);
                var requireDynamicallyAccessedMembersAction = new RequireDynamicallyAccessedMembersAction(typeNameResolver, in diagnosticContext, reflectionAccessAnalyzer);
                var genericArgumentDataFlow = new GenericArgumentDataFlow(requireDynamicallyAccessedMembersAction);
                switch (GenericInstantiation)
                {
                    case INamedTypeSymbol type:
                        genericArgumentDataFlow.ProcessGenericArgumentDataFlow(type);
                        break;

                    case IMethodSymbol method:
                        genericArgumentDataFlow.ProcessGenericArgumentDataFlow(method);
                        break;

                    case IFieldSymbol field:
                        genericArgumentDataFlow.ProcessGenericArgumentDataFlow(field);
                        break;
                }
            }
        }
    }
}
