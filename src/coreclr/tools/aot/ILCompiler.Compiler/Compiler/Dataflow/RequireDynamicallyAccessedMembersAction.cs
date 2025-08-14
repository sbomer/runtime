// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using ILCompiler.Dataflow;
using ILLink.Shared.TypeSystemProxy;
using Internal.TypeSystem;

#nullable enable

namespace ILLink.Shared.TrimAnalysis
{
//       Failed Mono.Linker.Tests.TestCases.All.DataFlow(t: "ApplyTypeAnnotations") [364 ms]
//   Error Message:
//    Method `Mono.Linker.Tests.Cases.DataFlow.ApplyTypeAnnotations.FromStringConstantWithGenericInnerInner.Method()' should have been kept
//   Stack Trace:
//      at Mono.Linker.Tests.TestCasesRunner.AssemblyChecker.Verify() in /home/svbomer/src/runtime/src/coreclr/tools/aot/ILCompiler.Trimming.Tests/TestCasesRunner/AssemblyChecker.cs:line 95
//    at Mono.Linker.Tests.TestCasesRunner.ResultChecker.Check(TrimmedTestCaseResult testResult) in /home/svbomer/src/runtime/src/coreclr/tools/aot/ILCompiler.Trimming.Tests/TestCasesRunner/ResultChecker.cs:line 71
//    at Mono.Linker.Tests.TestCases.All.Run(String testName) in /home/svbomer/src/runtime/src/coreclr/tools/aot/ILCompiler.Trimming.Tests/TestCases/TestSuites.cs:line 157
//    at Mono.Linker.Tests.TestCases.All.DataFlow(String t) in /home/svbomer/src/runtime/src/coreclr/tools/aot/ILCompiler.Trimming.Tests/TestCases/TestSuites.cs:line 16
//    at InvokeStub_All.DataFlow(Object, Span`1)
//    at System.Reflection.RuntimeMethodInfo.Invoke(Object obj, BindingFlags invokeAttr, Binder binder, Object[] parameters, CultureInfo culture)
    internal partial struct RequireDynamicallyAccessedMembersAction
    {
        private readonly ReflectionMarker _reflectionMarker;
        private readonly string _reason;

        public RequireDynamicallyAccessedMembersAction(
            ReflectionMarker reflectionMarker,
            in DiagnosticContext diagnosticContext,
            string reason)
        {
            _reflectionMarker = reflectionMarker;
            _diagnosticContext = diagnosticContext;
            _reason = reason;
        }

        public partial bool TryResolveTypeNameAndMark(string typeName, bool needsAssemblyName, out TypeProxy type)
        {
            if (_reflectionMarker.TryResolveTypeNameAndMark(typeName, _diagnosticContext, needsAssemblyName, _reason, out TypeDesc? foundType))
            {
                if (GenericArgumentDataFlow.RequiresGenericArgumentDataFlow(_reflectionMarker.Annotations, foundType))
                {
                    GenericArgumentDataFlow.ProcessGenericArgumentDataFlow(_diagnosticContext, _reflectionMarker, foundType);
                }

                type = new(foundType);
                return true;
            }
            else
            {
                type = default;
                return false;
            }
        }

        private partial void MarkTypeForDynamicallyAccessedMembers(in TypeProxy type, DynamicallyAccessedMemberTypes dynamicallyAccessedMemberTypes)
        {
            _reflectionMarker.MarkTypeForDynamicallyAccessedMembers(_diagnosticContext.Origin, type.Type, dynamicallyAccessedMemberTypes, _reason);
        }
    }
}
