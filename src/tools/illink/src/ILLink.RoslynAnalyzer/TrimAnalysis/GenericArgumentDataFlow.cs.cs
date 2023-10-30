// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using ILLink.Shared.TrimAnalysis;
using ILLink.Shared.TypeSystemProxy;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
	internal static class GenericArgumentDataFlow
	{
		public static bool RequiresGenericArgumentDataFlow (ISymbol symbol)
		{
			ImmutableArray<ITypeParameterSymbol> typeParameters;
			ImmutableArray<ITypeSymbol> typeArguments;

			switch (symbol) {
			case IMethodSymbol method:
				// We only need to validate static methods and then all generic methods
				// Instance non-generic methods don't need validation because the creation of the instance
				// is the place where the validation will happen.
				if (!method.IsStatic && !method.IsGenericMethod)
					return false;

				if (RequiresGenericArgumentDataFlow (method.ContainingType))
					return true;
				
				typeParameters = method.TypeParameters;
				typeArguments = method.TypeArguments;
				break;
			case INamedTypeSymbol type:
				typeParameters = type.TypeParameters;
				typeArguments = type.TypeArguments;
				break;
			case ITypeParameterSymbol:
			case IArrayTypeSymbol:
				return false;
			default:
				Debug.Fail ($"Unexpected symbol type: {symbol.GetType()}, {symbol}");
				return false;
			}

			foreach (var typeParameter in typeParameters) {
				var genericParameterProxy = new GenericParameterProxy (typeParameter);
				if (FlowAnnotations.Instance.GetGenericParameterValue (genericParameterProxy).DynamicallyAccessedMemberTypes != DynamicallyAccessedMemberTypes.None)
					return true;
			}

			// Method callsites can contain generic instantiations which may contain annotations inside nested generics
			// so we have to check all of the instantiations for that case.
			// For example:
			//   OuterGeneric<InnerGeneric<Annotated>>.Method<InnerGeneric<AnotherAnnotated>>();
			foreach (var typeArgument in typeArguments) {
				if (RequiresGenericArgumentDataFlow (typeArgument))
					return true;
			}

			return false;
		}


		public static void ProcessGenericArgumentDataFlow (DiagnosticContext diagnosticContext, ISymbol symbol, Location location)
		{
			ImmutableArray<ITypeParameterSymbol> typeParameters;
			ImmutableArray<ITypeSymbol> typeArguments;

			switch (symbol) {
			case IMethodSymbol method:
				typeParameters = method.TypeParameters;
				typeArguments = method.TypeArguments;
				break;
			case INamedTypeSymbol type:
				typeParameters = type.TypeParameters;
				typeArguments = type.TypeArguments;
				break;
			default:
				Debug.Fail ($"Unexpected symbol type: {symbol.GetType()}, {symbol}");
				return;
			}

			if (typeParameters == null) {
				Debug.Fail ($"Unexpected null type parameters for {symbol}");
				return;
			}

			for (int i = 0; i < typeParameters.Length; i++) {
				var sourceValue = SingleValueExtensions.FromTypeSymbol (typeArguments[i])!;
				var targetValue = new GenericParameterValue (typeParameters[i]);
				foreach (var diagnostic in DynamicallyAccessedMembersAnalyzer.GetDynamicallyAccessedMembersDiagnostics (sourceValue, targetValue, location))
					diagnosticContext.AddDiagnostic (diagnostic);
			}
		}
	}
}
