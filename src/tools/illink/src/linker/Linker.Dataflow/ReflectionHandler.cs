// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ILLink.Shared;
using ILLink.Shared.DataFlow;
using ILLink.Shared.TrimAnalysis;
using ILLink.Shared.TypeSystemProxy;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Linker.Steps;
using MultiValue = ILLink.Shared.DataFlow.ValueSet<ILLink.Shared.DataFlow.SingleValue>;

namespace Mono.Linker.Dataflow
{
	sealed class ReflectionHandler
	{
		private MessageOrigin _origin;
		private readonly LinkContext _context;
		private readonly ReflectionMarker _reflectionMarker;
		private readonly FlowAnnotations _annotations;
		private readonly MarkStep _markStep;
		private readonly TrimAnalysisPatternStore _trimAnalysisPatternStore;
		private static ValueSetLattice<SingleValue> MultiValueLattice => default;


		public ReflectionHandler (LinkContext context, MarkStep parent, MessageOrigin origin, TrimAnalysisPatternStore trimAnalysisPatternStore)
		{
			_context = context;
			_markStep = parent;
			_origin = origin;
			_reflectionMarker = new ReflectionMarker (context, parent, enabled: false);
			_annotations = context.Annotations.FlowAnnotations;
			_trimAnalysisPatternStore = trimAnalysisPatternStore;

		}

		public void HandleStoreField (FieldValue field, Instruction operation, MultiValue valueToStore)
			=> HandleStoreValueWithDynamicallyAccessedMembers (field, operation, valueToStore);

		public void HandleStoreParameter (MethodParameterValue parameter, Instruction operation, MultiValue valueToStore)
			=> HandleStoreValueWithDynamicallyAccessedMembers (parameter, operation, valueToStore);

		public void HandleStoreMethodReturnValue (MethodReturnValue returnValue, Instruction operation, MultiValue valueToStore)
			=> HandleStoreValueWithDynamicallyAccessedMembers (returnValue, operation, valueToStore);

		public MultiValue HandleCall (
			MethodBody callingMethodBody,
			MethodReference calledMethod,
			Instruction operation,
			ValueNodeList methodParams)
		{
			var reflectionProcessed = _markStep.ProcessReflectionDependency (callingMethodBody, operation);
			if (reflectionProcessed) {
				return UnknownValue.Instance;
			}

			Debug.Assert (callingMethodBody.Method == _origin.Provider);
			var calledMethodDefinition = _context.TryResolve (calledMethod);
			if (calledMethodDefinition == null) {
				return UnknownValue.Instance;
			}

			_origin = _origin.WithInstructionOffset (operation.Offset);

			MultiValue instanceValue;
			ImmutableArray<MultiValue> arguments;
			if (calledMethodDefinition.HasImplicitThis ()) {
				instanceValue = methodParams[0];
				arguments = methodParams.Skip (1).ToImmutableArray ();
			} else {
				instanceValue = MultiValueLattice.Top;
				arguments = methodParams.ToImmutableArray ();
			}

			_trimAnalysisPatternStore.Add (new TrimAnalysisMethodCallPattern (
				operation,
				calledMethod,
				instanceValue,
				arguments,
				_origin
			));

			var diagnosticContext = new DiagnosticContext (_origin, diagnosticsEnabled: false, _context);
			return HandleCall (
				operation,
				calledMethod,
				instanceValue,
				arguments,
				diagnosticContext,
				_reflectionMarker,
				_context,
				_markStep);
		}

		public static MultiValue HandleCall (
			Instruction operation,
			MethodReference calledMethod,
			MultiValue instanceValue,
			ImmutableArray<MultiValue> argumentValues,
			DiagnosticContext diagnosticContext,
			ReflectionMarker reflectionMarker,
			LinkContext context,
			MarkStep markStep)
		{
			var origin = diagnosticContext.Origin;
			var calledMethodDefinition = context.TryResolve (calledMethod);
			Debug.Assert (calledMethodDefinition != null);
			var callingMethodDefinition = origin.Provider as MethodDefinition;
			Debug.Assert (callingMethodDefinition != null);

			bool requiresDataFlowAnalysis = context.Annotations.FlowAnnotations.RequiresDataFlowAnalysis (calledMethodDefinition);
			bool isNewObj = operation.OpCode.Code == Code.Newobj;
			var annotatedMethodReturnValue = context.Annotations.FlowAnnotations.GetMethodReturnValue (calledMethodDefinition, isNewObj);
			Debug.Assert (requiresDataFlowAnalysis || annotatedMethodReturnValue.DynamicallyAccessedMemberTypes == DynamicallyAccessedMemberTypes.None);

			var handleCallAction = new HandleCallAction (context, operation, markStep, reflectionMarker, diagnosticContext, callingMethodDefinition, calledMethod);
			var intrinsicId = Intrinsics.GetIntrinsicIdForMethod (calledMethodDefinition);
			if (!handleCallAction.Invoke (callingMethodDefinition, instanceValue, argumentValues, intrinsicId, out MultiValue methodReturnValue))
				throw new NotImplementedException ($"Unhandled intrinsic: {intrinsicId}");
			return methodReturnValue;
		}

		public MultiValue GetFieldValue (FieldDefinition field) => _annotations.GetFieldValue (field);

		private void HandleStoreValueWithDynamicallyAccessedMembers (ValueWithDynamicallyAccessedMembers targetValue, Instruction operation, MultiValue sourceValue)
		{
			if (targetValue.DynamicallyAccessedMemberTypes != 0) {
				_origin = _origin.WithInstructionOffset (operation.Offset);
				HandleAssignmentPattern (sourceValue, targetValue);
			}
		}

		public void HandleAssignmentPattern (
			in MultiValue value,
			ValueWithDynamicallyAccessedMembers targetValue)
		{
			_trimAnalysisPatternStore.Add (new TrimAnalysisAssignmentPattern (value, targetValue, _origin));
		}

		public ValueWithDynamicallyAccessedMembers GetMethodThisParameterValue (MethodDefinition method)
			=> _annotations.GetMethodThisParameterValue (method);

		public ValueWithDynamicallyAccessedMembers GetMethodParameterValue (ParameterProxy parameter)
			=> GetMethodParameterValue (parameter, _annotations.GetParameterAnnotation (parameter));

		MethodParameterValue GetMethodParameterValue (ParameterProxy parameter, DynamicallyAccessedMemberTypes dynamicallyAccessedMemberTypes)
		{
			return _annotations.GetMethodParameterValue (parameter, dynamicallyAccessedMemberTypes);
		}

		public static void WarnAboutInvalidILInMethod (MethodBody method, int ilOffset)
		{
			// Serves as a debug helper to make sure valid IL is not considered invalid.
			//
			// The .NET Native compiler used to warn if it detected invalid IL during treeshaking,
			// but the warnings were often triggered in autogenerated dead code of a major game engine
			// and resulted in support calls. No point in warning. If the code gets exercised at runtime,
			// an InvalidProgramException will likely be raised.
			Debug.WriteLine ($"{method}, IL{ilOffset}: Invalid IL detected");
			Debug.Fail ("Invalid IL or a bug in the scanner");
		}

		public void HandleReturnValue (MethodDefinition method, MultiValue returnValue)
		{
			if (!method.ReturnsVoid ()) {
				var methodReturnValue = _annotations.GetMethodReturnValue (method, isNewObj: false);
				if (methodReturnValue.DynamicallyAccessedMemberTypes != 0)
					HandleAssignmentPattern (returnValue, methodReturnValue);
			}
		}
	}
}
