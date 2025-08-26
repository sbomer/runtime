// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ILLink.RoslynAnalyzer.DataFlow;
using ILLink.Shared;
using ILLink.Shared.TrimAnalysis;
using Microsoft.CodeAnalysis;

namespace ILLink.RoslynAnalyzer.TrimAnalysis
{
    readonly struct ReflectionAccessAnalyzer
    {
        readonly DiagnosticContext _diagnosticContext;

        readonly INamedTypeSymbol? _typeHierarchyType;

        readonly TypeNameResolver _typeNameResolver;

        public ReflectionAccessAnalyzer(
            in DiagnosticContext diagnosticContext,
            TypeNameResolver typeNameResolver,
            INamedTypeSymbol? typeHierarchyType)
        {
            _diagnosticContext = diagnosticContext;
            _typeHierarchyType = typeHierarchyType;
            _typeNameResolver = typeNameResolver;
        }

#pragma warning disable CA1822 // Mark members as static - the other partial implementations might need to be instance methods
        internal void GetReflectionAccessDiagnostics(ITypeSymbol typeSymbol, DynamicallyAccessedMemberTypes requiredMemberTypes, bool declaredOnly = false)
        {
            typeSymbol = typeSymbol.OriginalDefinition;
            foreach (var member in typeSymbol.GetDynamicallyAccessedMembers(requiredMemberTypes, declaredOnly))
            {
                switch (member)
                {
                    case IMethodSymbol method:
                        GetReflectionAccessDiagnosticsForMethod(method);
                        break;
                    case IFieldSymbol field:
                        GetDiagnosticsForField(field);
                        break;
                    case IPropertySymbol property:
                        GetReflectionAccessDiagnosticsForProperty(property);
                        break;
                    /* Skip Type and InterfaceImplementation marking since doesnt seem relevant for diagnostic generation
                    case ITypeSymbol nestedType:
                        MarkType(location, nestedType);
                        break;
                    case InterfaceImplementation interfaceImplementation:
                        MarkInterfaceImplementation(location, interfaceImplementation, dependencyKind);
                        break;
                    */
                    case IEventSymbol @event:
                        GetDiagnosticsForEvent(@event);
                        break;
                }
            }
        }

        internal void GetReflectionAccessDiagnosticsForEventsOnTypeHierarchy(ITypeSymbol typeSymbol, string name, BindingFlags? bindingFlags)
        {
            foreach (var @event in typeSymbol.GetEventsOnTypeHierarchy(e => e.Name == name, bindingFlags))
                GetDiagnosticsForEvent(@event);
        }

        internal void GetReflectionAccessDiagnosticsForFieldsOnTypeHierarchy(ITypeSymbol typeSymbol, string name, BindingFlags? bindingFlags)
        {
            foreach (var field in typeSymbol.GetFieldsOnTypeHierarchy(f => f.Name == name, bindingFlags))
                GetDiagnosticsForField(field);
        }

        internal void GetReflectionAccessDiagnosticsForPropertiesOnTypeHierarchy(ITypeSymbol typeSymbol, string name, BindingFlags? bindingFlags)
        {
            foreach (var prop in typeSymbol.GetPropertiesOnTypeHierarchy(p => p.Name == name, bindingFlags))
                GetReflectionAccessDiagnosticsForProperty(prop);
        }

        internal void GetReflectionAccessDiagnosticsForConstructorsOnType(ITypeSymbol typeSymbol, BindingFlags? bindingFlags, int? parameterCount)
        {
            foreach (var c in typeSymbol.GetConstructorsOnType(filter: parameterCount.HasValue ? c => c.Parameters.Length == parameterCount.Value : null, bindingFlags: bindingFlags))
                GetReflectionAccessDiagnosticsForMethod(c);
        }

        internal void GetReflectionAccessDiagnosticsForPublicParameterlessConstructor(ITypeSymbol typeSymbol)
        {
            foreach (var c in typeSymbol.GetConstructorsOnType(filter: m => (m.DeclaredAccessibility == Accessibility.Public) && m.Parameters.Length == 0))
                GetReflectionAccessDiagnosticsForMethod(c);
        }

        private void ReportRequiresUnreferencedCodeDiagnostic(AttributeData requiresAttributeData, ISymbol member)
        {
            var message = RequiresUnreferencedCodeUtils.GetMessageFromAttribute(requiresAttributeData);
            var url = RequiresAnalyzerBase.GetUrlFromAttribute(requiresAttributeData);
            _diagnosticContext.AddDiagnostic(DiagnosticId.RequiresUnreferencedCode, member.GetDisplayName(), message, url);
        }

        internal void GetReflectionAccessDiagnosticsForMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.ToString().Contains("NewConstraintTestType"))
                System.Diagnostics.Debug.WriteLine("H");
            if (_typeHierarchyType is not null)
            {
                GetTypeHierarchyReflectionAccessDiagnostics(methodSymbol);
                return;
            }

            if (methodSymbol.IsInRequiresUnreferencedCodeAttributeScope(out var requiresUnreferencedCodeAttributeData))
            {
                ReportRequiresUnreferencedCodeDiagnostic(requiresUnreferencedCodeAttributeData, methodSymbol);
            }
            else

            // foreach (var requiresAnalyzer in _diagnosticContext.Context.EnabledRequiresAnalyzers)
            // {
            // if (_diagnosticContext.FeatureContext.IsEnabled(requiresAnalyzer.RequiresAttributeFullyQualifiedName))
            //     continue;

            // // TODO: ensure we check the suppression scope correctly here. Not just of the
            // // methodSymbol that's being reflection-accessed, but of the context.
            // // from _diagnosticContext I guess.
            // if (_diagnosticContext.OwningSymbol.IsInRequiresScope(requiresAnalyzer.RequiresAttributeFullyQualifiedName, out _))
            //     continue;

            // if (methodSymbol.IsInRequiresScope(requiresAnalyzer.RequiresAttributeFullyQualifiedName, out var requiresAttributeData))
            // {
            //     requiresAnalyzer.CreateRequiresDiagnostic(methodSymbol, requiresAttributeData, in _diagnosticContext);
            // }
            // }

            // Below is about accessing DAM annotated members, so only RUC is applicable as a suppression scope
            // if (!methodSymbol.IsInRequiresUnreferencedCodeAttributeScope(out _))
            {
                GetDiagnosticsForReflectionAccessToDAMOnMethod(methodSymbol);
            }
        }

        internal void GetTypeHierarchyReflectionAccessDiagnostics(ISymbol member)
        {
            Debug.Assert(member is IMethodSymbol or IFieldSymbol);

            // Don't check whether the current scope is a RUC type or RUC method because these warnings
            // are not suppressed in RUC scopes. Here the scope represents the DynamicallyAccessedMembers
            // annotation on a type, not a callsite which uses the annotation. We always want to warn about
            // possible reflection access indicated by these annotations.

            Debug.Assert(_typeHierarchyType is not null);

            static bool IsDeclaredWithinType(ISymbol member, INamedTypeSymbol type)
            {
                INamedTypeSymbol containingType = member.ContainingType;
                while (containingType is not null)
                {
                    if (SymbolEqualityComparer.Default.Equals(containingType, type))
                        return true;

                    containingType = containingType.ContainingType;
                }
                return false;
            }

            var location = _diagnosticContext.Location;
            var reportOnMember = IsDeclaredWithinType(member, _typeHierarchyType!);
            if (reportOnMember)
                location = DynamicallyAccessedMembersAnalyzer.GetPrimaryLocation(member.Locations);

            var diagnosticContext = new DiagnosticContext(location, _typeHierarchyType!, _diagnosticContext.ReportDiagnostic, _diagnosticContext.Context, _diagnosticContext.FeatureContext);

            if (member.IsInRequiresUnreferencedCodeAttributeScope(out AttributeData? requiresUnreferencedCodeAttribute))
            {
                var id = reportOnMember ? DiagnosticId.DynamicallyAccessedMembersOnTypeReferencesMemberWithRequiresUnreferencedCode : DiagnosticId.DynamicallyAccessedMembersOnTypeReferencesMemberOnBaseWithRequiresUnreferencedCode;
                diagnosticContext.AddDiagnostic(id, _typeHierarchyType!.GetDisplayName(),
                    member.GetDisplayName(),
                    MessageFormat.FormatRequiresAttributeMessageArg(RequiresUnreferencedCodeUtils.GetMessageFromAttribute(requiresUnreferencedCodeAttribute)),
                    MessageFormat.FormatRequiresAttributeMessageArg(RequiresAnalyzerBase.GetUrlFromAttribute(requiresUnreferencedCodeAttribute)));
            }

            if (FlowAnnotations.ShouldWarnWhenAccessedForReflection(member))
            {
                var id = reportOnMember ? DiagnosticId.DynamicallyAccessedMembersOnTypeReferencesMemberWithDynamicallyAccessedMembers : DiagnosticId.DynamicallyAccessedMembersOnTypeReferencesMemberOnBaseWithDynamicallyAccessedMembers;
                diagnosticContext.AddDiagnostic(id, _typeHierarchyType!.GetDisplayName(), member.GetDisplayName());
            }
        }

        internal void GetDiagnosticsForReflectionAccessToDAMOnMethod(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.IsVirtual && FlowAnnotations.GetMethodReturnValueAnnotation(methodSymbol) != DynamicallyAccessedMemberTypes.None)
            {
                _diagnosticContext.AddDiagnostic(DiagnosticId.DynamicallyAccessedMembersMethodAccessedViaReflection, methodSymbol.GetDisplayName());
            }
            else
            {
                foreach (var parameter in methodSymbol.GetParameters())
                {
                    if (FlowAnnotations.GetMethodParameterAnnotation(parameter) != DynamicallyAccessedMemberTypes.None)
                    {
                        _diagnosticContext.AddDiagnostic(DiagnosticId.DynamicallyAccessedMembersMethodAccessedViaReflection, methodSymbol.GetDisplayName());
                        break;
                    }
                }
            }
        }

        internal void GetReflectionAccessDiagnosticsForProperty(IPropertySymbol propertySymbol)
        {
            if (propertySymbol.SetMethod is not null)
                GetReflectionAccessDiagnosticsForMethod(propertySymbol.SetMethod);
            if (propertySymbol.GetMethod is not null)
                GetReflectionAccessDiagnosticsForMethod(propertySymbol.GetMethod);
        }

        private void GetDiagnosticsForEvent(IEventSymbol eventSymbol)
        {
            if (eventSymbol.AddMethod is not null)
                GetReflectionAccessDiagnosticsForMethod(eventSymbol.AddMethod);
            if (eventSymbol.RemoveMethod is not null)
                GetReflectionAccessDiagnosticsForMethod(eventSymbol.RemoveMethod);
            if (eventSymbol.RaiseMethod is not null)
                GetReflectionAccessDiagnosticsForMethod(eventSymbol.RaiseMethod);
        }

        private void GetDiagnosticsForField(IFieldSymbol fieldSymbol)
        {
            if (_typeHierarchyType is not null)
            {
                GetTypeHierarchyReflectionAccessDiagnostics(fieldSymbol);
                return;
            }

            if (fieldSymbol.TryGetRequiresUnreferencedCodeAttribute(out var requiresUnreferencedCodeAttributeData))
                ReportRequiresUnreferencedCodeDiagnostic(requiresUnreferencedCodeAttributeData, fieldSymbol);

            if (FlowAnnotations.GetFieldAnnotation(fieldSymbol) != DynamicallyAccessedMemberTypes.None)
            {
                _diagnosticContext.AddDiagnostic(DiagnosticId.DynamicallyAccessedMembersFieldAccessedViaReflection, fieldSymbol.GetDisplayName());
            }
        }

        internal bool TryResolveTypeNameAndMark(string typeName, bool needsAssemblyName, [NotNullWhen(true)] out ITypeSymbol? type)
        {
            return _typeNameResolver.TryResolveTypeName(typeName, _diagnosticContext, out type, needsAssemblyName);
        }
    }
}
