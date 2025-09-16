// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ILLink.Shared.TrimAnalysis;
using Mono.Cecil;
using Mono.Linker.Steps;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;

namespace Mono.Linker.Dataflow
{
    internal readonly struct GenericArgumentDataFlow
    {
        private readonly DiagnosticContext _diagnosticContext;
        private readonly ReflectionMarker _reflectionMarker;
        private readonly LinkContext _context;

        public GenericArgumentDataFlow(in MessageOrigin origin, MarkStep markStep, LinkContext context)
        {
            _context = context;
            _diagnosticContext = new DiagnosticContext(origin, !context.Annotations.ShouldSuppressAnalysisWarningsForRequiresUnreferencedCode(origin.Provider, out _), context);
            _reflectionMarker = new ReflectionMarker(context, markStep, enabled: true);
        }

        public GenericArgumentDataFlow(in DiagnosticContext diagnosticContext, ReflectionMarker reflectionMarker, LinkContext context)
        {
            _diagnosticContext = diagnosticContext;
            _reflectionMarker = reflectionMarker;
            _context = context;
        }

        public void ProcessGenericArgumentDataFlow(TypeReference type)
        {
            if (type is GenericInstanceType genericInstanceType && _context.TryResolve(type) is TypeDefinition typeDefinition)
            {
                ProcessGenericInstantiation(genericInstanceType, typeDefinition);
            }
        }

        public void ProcessGenericArgumentDataFlow(MethodReference method)
        {
            if (method is GenericInstanceMethod genericInstanceMethod && _context.TryResolve(method) is MethodDefinition methodDefinition)
            {
                ProcessGenericInstantiation(genericInstanceMethod, methodDefinition);
            }

            ProcessGenericArgumentDataFlow(method.DeclaringType);
        }

        public void ProcessGenericArgumentDataFlow(FieldReference field)
        {
            ProcessGenericArgumentDataFlow(field.DeclaringType);
        }

        private void ProcessGenericInstantiation(IGenericInstance genericInstance, IGenericParameterProvider genericParameterProvider)
        {
            var arguments = genericInstance.GenericArguments;
            var parameters = genericParameterProvider.GenericParameters;

            for (int i = 0; i < arguments.Count; i++)
            {
                var genericArgument = arguments[i];
                var genericParameter = parameters[i];

                var parameterRequirements = _context.Annotations.FlowAnnotations.GetGenericParameterAnnotation(genericParameter);

                if (genericParameter.HasDefaultConstructorConstraint)
                {
                    _reflectionMarker.MarkTypeForDynamicallyAccessedMembers(_diagnosticContext.Origin, genericArgument, DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, DependencyKind.DefaultCtorForNewConstrainedGenericArgument);
                    // Avoid duplicate warnings for new() and DAMT.PublicParameterlessConstructor
                    parameterRequirements &= ~DynamicallyAccessedMemberTypes.PublicParameterlessConstructor;
                }

                var genericParameterValue = _context.Annotations.FlowAnnotations.GetGenericParameterValue(genericParameter, parameterRequirements);
                if (genericParameterValue.DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
                {
                    MultiValue genericArgumentValue = _context.Annotations.FlowAnnotations.GetTypeValueFromGenericArgument(genericArgument);

                    var requireDynamicallyAccessedMembersAction = new RequireDynamicallyAccessedMembersAction(_context, _reflectionMarker, _diagnosticContext);
                    requireDynamicallyAccessedMembersAction.Invoke(genericArgumentValue, genericParameterValue);
                }

                // Recursively process generic argument data flow on the generic argument if it itself is generic
                if (genericArgument.IsGenericInstance)
                {
                    ProcessGenericArgumentDataFlow(genericArgument);
                }
            }
        }

        internal static bool RequiresGenericArgumentDataFlow(FlowAnnotations flowAnnotations, MethodReference method)
        {
            // Method callsites can contain generic instantiations which may contain annotations inside nested generics
            // so we have to check all of the instantiations for that case.
            // For example:
            //   OuterGeneric<InnerGeneric<Annotated>>.Method<InnerGeneric<AnotherAnnotated>>();

            if (method is GenericInstanceMethod genericInstanceMethod)
            {
                if (flowAnnotations.HasGenericParameterAnnotation(method))
                    return true;

                if (flowAnnotations.HasGenericParameterNewConstraint(method))
                    return true;

                foreach (var genericArgument in genericInstanceMethod.GenericArguments)
                {
                    if (RequiresGenericArgumentDataFlow(flowAnnotations, genericArgument))
                        return true;
                }
            }

            return RequiresGenericArgumentDataFlow(flowAnnotations, method.DeclaringType);
        }

        internal static bool RequiresGenericArgumentDataFlow(FlowAnnotations flowAnnotations, FieldReference field)
        {
            return RequiresGenericArgumentDataFlow(flowAnnotations, field.DeclaringType);
        }

        internal static bool RequiresGenericArgumentDataFlow(FlowAnnotations flowAnnotations, TypeReference type)
        {
            if (flowAnnotations.HasGenericParameterAnnotation(type))
            {
                return true;
            }

            if (flowAnnotations.HasGenericParameterNewConstraint(type))
            {
                return true;
            }

            if (type is GenericInstanceType genericInstanceType)
            {
                foreach (var genericArgument in genericInstanceType.GenericArguments)
                {
                    if (RequiresGenericArgumentDataFlow(flowAnnotations, genericArgument))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
