// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using ILLink.Shared.TrimAnalysis;
using Mono.Cecil;
using Mono.Linker.Steps;

namespace Mono.Linker.Dataflow
{
    public readonly record struct TrimAnalysisGenericInstantiationAccessPattern
    {
        public readonly MemberReference MemberReference;
        public readonly MessageOrigin Origin;

        internal TrimAnalysisGenericInstantiationAccessPattern(MemberReference memberReference, MessageOrigin origin)
        {
            MemberReference = memberReference;
            Origin = origin;
        }

        public void MarkAndProduceDiagnostics(ReflectionMarker reflectionMarker, MarkStep markStep, LinkContext context)
        {
            bool diagnosticsEnabled = !context.Annotations.ShouldSuppressAnalysisWarningsForRequiresUnreferencedCode(Origin.Provider, out _);
            var diagnosticContext = new DiagnosticContext(Origin, diagnosticsEnabled, context);

            var genericArgumentDataFlow = new GenericArgumentDataFlow(in diagnosticContext, reflectionMarker, context);
            switch (MemberReference)
            {
                case TypeReference typeReference:
                    genericArgumentDataFlow.ProcessGenericArgumentDataFlow(typeReference);
                    break;

                case MethodReference methodReference:
                    genericArgumentDataFlow.ProcessGenericArgumentDataFlow(methodReference);
                    break;

                case FieldReference fieldReference:
                    genericArgumentDataFlow.ProcessGenericArgumentDataFlow(fieldReference);
                    break;
            }
        }
    }
}
