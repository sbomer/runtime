// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Metadata;

using ILCompiler.Dataflow;
using ILCompiler.DependencyAnalysisFramework;
using ILCompiler.Logging;

using ILLink.Shared;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ReflectionDependencyAttributesOnEntityNode : DependencyNodeCore<NodeFactory>
    {
        private const string DynamicDependencyNamespace = "System.Diagnostics.CodeAnalysis";
        private const string DynamicDependencyName = "DynamicDependencyAttribute";
        private const string PreserveDependencyNamespace = "System.Runtime.CompilerServices";
        private const string PreserveDependencyName = "PreserveDependencyAttribute";

        private readonly TypeSystemEntity _entity;

        public ReflectionDependencyAttributesOnEntityNode(TypeSystemEntity entity)
        {
            Debug.Assert(entity is EcmaMethod or EcmaField);
            _entity = entity;
        }

        public static void AddDependenciesDueToAttributes(
            ref DependencyList dependencies,
            NodeFactory factory,
            TypeSystemEntity entity)
        {
            bool hasDependencyAttribute = entity switch
            {
                EcmaMethod method => method.HasCustomAttribute(DynamicDependencyNamespace, DynamicDependencyName)
                    || method.HasCustomAttribute(PreserveDependencyNamespace, PreserveDependencyName),
                EcmaField field => field.HasCustomAttribute(DynamicDependencyNamespace, DynamicDependencyName)
                    || field.HasCustomAttribute(PreserveDependencyNamespace, PreserveDependencyName),
                _ => false,
            };
            if (!hasDependencyAttribute)
            {
                return;
            }

            dependencies ??= new DependencyList();
            dependencies.Add(factory.ReflectionDependencyAttributes(entity), "Reflection dependency attribute");
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            DependencyList dependencies = null;

            try
            {
                TypeDesc owningType = _entity.GetOwningType();
                foreach (CustomAttributeValue<TypeDesc> attribute in GetDecodedCustomAttributes(
                    DynamicDependencyNamespace,
                    DynamicDependencyName))
                {
                    ProcessDynamicDependency(ref dependencies, factory, owningType, attribute);
                }

                foreach (CustomAttributeValue<TypeDesc> attribute in GetDecodedCustomAttributes(
                    PreserveDependencyNamespace,
                    PreserveDependencyName))
                {
                    ProcessPreserveDependency(ref dependencies, factory, owningType, attribute);
                }
            }
            catch (TypeSystemException)
            {
                factory.Logger.LogWarning(
                    new MessageOrigin(_entity),
                    DiagnosticId.DynamicDependencyAttributeCouldNotBeAnalyzed);
            }

            return dependencies;
        }

        private IEnumerable<CustomAttributeValue<TypeDesc>> GetDecodedCustomAttributes(string attributeNamespace, string attributeName)
            => _entity switch
            {
                EcmaMethod method => method.GetDecodedCustomAttributes(attributeNamespace, attributeName),
                EcmaField field => field.GetDecodedCustomAttributes(attributeNamespace, attributeName),
                _ => Array.Empty<CustomAttributeValue<TypeDesc>>(),
            };

        private void ProcessDynamicDependency(
            ref DependencyList dependencies,
            NodeFactory factory,
            TypeDesc owningType,
            CustomAttributeValue<TypeDesc> attribute)
        {
            if (!TryResolveDynamicDependencyTarget(
                    factory,
                    owningType,
                    attribute,
                    out MetadataType targetType,
                    out object memberSelector,
                    out ModuleDesc typeNameModule))
            {
                return;
            }

            if (typeNameModule is not null)
            {
                dependencies ??= new DependencyList();
                dependencies.Add(
                    factory.ReflectionVisibleModule(typeNameModule),
                    "DynamicDependencyAttribute type name");
            }

            IEnumerable<TypeSystemEntity> members = memberSelector switch
            {
                string memberSignature => DocumentationSignatureParser.GetMembersByDocumentationSignature(
                    targetType,
                    memberSignature,
                    acceptName: true),
                DynamicallyAccessedMemberTypes memberTypes => targetType.GetDynamicallyAccessedMembers(memberTypes),
                _ => Array.Empty<TypeSystemEntity>(),
            };

            MarkResolvedMembers(
                ref dependencies,
                factory,
                targetType,
                members,
                memberSelector.ToString() ?? "",
                DiagnosticId.NoMembersResolvedForMemberSignatureOrType,
                "DynamicDependencyAttribute");
        }

        private bool TryResolveDynamicDependencyTarget(
            NodeFactory factory,
            TypeDesc owningType,
            CustomAttributeValue<TypeDesc> attribute,
            [NotNullWhen(true)] out MetadataType targetType,
            [NotNullWhen(true)] out object memberSelector,
            out ModuleDesc typeNameModule)
        {
            targetType = null;
            memberSelector = null;
            typeNameModule = null;

            if (attribute.FixedArguments.Length == 0)
                return false;

            object firstArgument = attribute.FixedArguments[0].Value;
            memberSelector = firstArgument switch
            {
                string signature => signature,
                int memberTypes => (DynamicallyAccessedMemberTypes)memberTypes,
                _ => null,
            };

            if (memberSelector is null)
                return false;

            TypeDesc resolvedType;
            switch (attribute.FixedArguments.Length)
            {
                case 1 when memberSelector is string:
                    resolvedType = owningType;
                    break;

                case 2 when attribute.FixedArguments[1].Value is TypeDesc type:
                    resolvedType = type;
                    break;

                case 3 when attribute.FixedArguments[1].Value is string typeName
                    && attribute.FixedArguments[2].Value is string assemblyName:
                    if (!TryResolveType(factory, assemblyName, typeName, out resolvedType, out typeNameModule))
                    {
                        return false;
                    }
                    break;

                default:
                    return false;
            }

            while (resolvedType.IsParameterizedType)
                resolvedType = ((ParameterizedType)resolvedType).ParameterType;

            targetType = resolvedType.GetTypeDefinition() as MetadataType;
            return targetType is not null;
        }

        private void ProcessPreserveDependency(
            ref DependencyList dependencies,
            NodeFactory factory,
            TypeDesc owningType,
            CustomAttributeValue<TypeDesc> attribute)
        {
            factory.Logger.LogWarning(
                new MessageOrigin(_entity),
                DiagnosticId.DeprecatedPreserveDependencyAttribute);

            if (!ShouldProcessPreserveDependency(factory, attribute))
                return;

            if (attribute.FixedArguments.Length == 0
                || attribute.FixedArguments[0].Value is not string memberSignature)
            {
                return;
            }

            MetadataType targetType;
            if (attribute.FixedArguments.Length >= 2
                && attribute.FixedArguments[1].Value is string typeName)
            {
                string assemblyName = attribute.FixedArguments.Length >= 3
                    ? attribute.FixedArguments[2].Value as string
                    : null;
                ModuleDesc module = assemblyName is null
                    ? ((MetadataType)owningType.GetTypeDefinition()).Module
                    : ResolveAssembly(factory, assemblyName);
                if (module is null)
                {
                    factory.Logger.LogWarning(
                        new MessageOrigin(_entity),
                        DiagnosticId.CouldNotResolveDependencyAssembly,
                        assemblyName ?? "");
                    return;
                }

                TypeDesc resolvedType = module.GetTypeByCustomAttributeTypeName(typeName, throwIfNotFound: false);
                targetType = resolvedType?.GetTypeDefinition() as MetadataType;
                if (targetType is null)
                {
                    factory.Logger.LogWarning(
                        new MessageOrigin(_entity),
                        DiagnosticId.CouldNotResolveDependencyType,
                        typeName);
                    return;
                }
            }
            else
            {
                targetType = (MetadataType)owningType.GetTypeDefinition();
            }

            IEnumerable<TypeSystemEntity> members;
            if (memberSignature == "*")
            {
                members = targetType.GetDynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All);
            }
            else
            {
                string documentationSignature = ConvertPreserveDependencySignature(memberSignature);
                members = DocumentationSignatureParser.GetMembersByDocumentationSignature(
                    targetType,
                    documentationSignature,
                    acceptName: true);

                if (!members.Any())
                    members = MatchPreserveDependencyByNameAndArity(targetType, memberSignature);
            }

            MarkResolvedMembers(
                ref dependencies,
                factory,
                targetType,
                members,
                GetPreserveDependencyMemberName(memberSignature),
                DiagnosticId.CouldNotResolveDependencyMember,
                "PreserveDependencyAttribute");
        }

        private static bool ShouldProcessPreserveDependency(
            NodeFactory factory,
            CustomAttributeValue<TypeDesc> attribute)
        {
            foreach (CustomAttributeNamedArgument<TypeDesc> namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Name != "Condition" || namedArgument.Value is not string condition)
                    continue;

                return condition switch
                {
                    "" => true,
                    "DEBUG" => factory.Settings.KeepMembersForDebugger,
                    _ => false,
                };
            }

            return true;
        }

        private bool TryResolveType(
            NodeFactory factory,
            string assemblyName,
            string typeName,
            [NotNullWhen(true)] out TypeDesc type,
            [NotNullWhen(true)] out ModuleDesc module)
        {
            module = ResolveAssembly(factory, assemblyName);
            if (module is null)
            {
                factory.Logger.LogWarning(
                    new MessageOrigin(_entity),
                    DiagnosticId.UnresolvedAssemblyInDynamicDependencyAttribute,
                    assemblyName);
                type = null;
                return false;
            }

            type = DocumentationSignatureParser.GetTypeByDocumentationSignature((IAssemblyDesc)module, typeName);
            if (type is null)
            {
                factory.Logger.LogWarning(
                    new MessageOrigin(_entity),
                    DiagnosticId.UnresolvedTypeInDynamicDependencyAttribute,
                    typeName);
                return false;
            }

            return true;
        }

        private static string GetPreserveDependencyMemberName(string signature)
        {
            int parameterListStart = signature.IndexOf('(');
            return parameterListStart >= 0 ? signature[..parameterListStart].Trim() : signature.Trim();
        }

        private static ModuleDesc ResolveAssembly(NodeFactory factory, string assemblyName)
        {
            try
            {
                return factory.TypeSystemContext.ResolveAssembly(
                    AssemblyNameInfo.Parse(assemblyName),
                    throwIfNotFound: false);
            }
            catch (TypeSystemException)
            {
                return null;
            }
        }

        private void MarkResolvedMembers(
            ref DependencyList dependencies,
            NodeFactory factory,
            MetadataType targetType,
            IEnumerable<TypeSystemEntity> members,
            string memberDescription,
            DiagnosticId unresolvedDiagnostic,
            string reason)
        {
            TypeSystemEntity[] resolvedMembers = members.Distinct().ToArray();
            if (resolvedMembers.Length == 0)
            {
                factory.Logger.LogWarning(
                    new MessageOrigin(_entity),
                    unresolvedDiagnostic,
                    memberDescription,
                    targetType.GetDisplayName());
                return;
            }

            var marker = new ReflectionMarker(
                factory.Logger,
                factory,
                factory.FlowAnnotations,
                typeHierarchyDataFlowOrigin: null,
                enabled: true);
            foreach (TypeSystemEntity member in resolvedMembers)
                marker.MarkTypeSystemEntity(new MessageOrigin(_entity), member, reason);

            dependencies ??= new DependencyList();
            dependencies.AddRange(marker.Dependencies);
        }

        private static string ConvertPreserveDependencySignature(string signature)
        {
            string converted = signature.Replace(" ", "", StringComparison.Ordinal)
                .Replace('&', '@')
                .Replace('+', '.');

            if (converted.StartsWith(".ctor", StringComparison.Ordinal))
                return string.Concat("#ctor", converted.AsSpan(".ctor".Length));
            if (converted.StartsWith(".cctor", StringComparison.Ordinal))
                return string.Concat("#cctor", converted.AsSpan(".cctor".Length));

            int arityMarker = converted.IndexOf('`');
            if (arityMarker >= 0
                && arityMarker + 1 < converted.Length
                && converted[arityMarker + 1] != '`')
            {
                converted = converted.Insert(arityMarker, "`");
            }

            return converted;
        }

        private static IEnumerable<TypeSystemEntity> MatchPreserveDependencyByNameAndArity(
            MetadataType type,
            string signature)
        {
            string memberName = signature.Replace(" ", "", StringComparison.Ordinal);
            int parameterListStart = memberName.IndexOf('(');
            string[] parameters = null;
            if (parameterListStart >= 0 && memberName.EndsWith(')'))
            {
                string parameterList = memberName[(parameterListStart + 1)..^1];
                parameters = SplitParameters(parameterList);
                memberName = memberName[..parameterListStart];
            }

            int arity = 0;
            int arityMarker = memberName.IndexOf('`');
            if (arityMarker > 0)
            {
                int.TryParse(memberName.AsSpan(arityMarker + 1), out arity);
                memberName = memberName[..arityMarker];
            }

            foreach (MethodDesc method in type.GetMethods())
            {
                if (!method.Name.StringEquals(memberName)
                    || method.Instantiation.Length != arity
                    || (parameters is not null && !ParametersMatch(method, parameters)))
                {
                    continue;
                }

                yield return method;
            }

            foreach (FieldDesc field in type.GetFields())
            {
                if (field.Name.StringEquals(memberName))
                    yield return field;
            }

            static string[] SplitParameters(string parameterList)
            {
                if (parameterList.Length == 0)
                    return Array.Empty<string>();

                var parameters = new List<string>();
                int start = 0;
                int nesting = 0;
                for (int i = 0; i < parameterList.Length; i++)
                {
                    nesting += parameterList[i] switch
                    {
                        '[' or '{' => 1,
                        ']' or '}' => -1,
                        _ => 0,
                    };

                    if (parameterList[i] == ',' && nesting == 0)
                    {
                        parameters.Add(parameterList[start..i]);
                        start = i + 1;
                    }
                }

                parameters.Add(parameterList[start..]);
                return parameters.ToArray();
            }

            static bool ParametersMatch(MethodDesc method, string[] parameters)
            {
                if (method.Signature.Length != parameters.Length)
                    return false;

                var formatter = new CustomAttributeTypeNameFormatter();
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (!TypeNameMatches(method.Signature[i], parameters[i], formatter))
                        return false;
                }

                return true;
            }

            static bool TypeNameMatches(
                TypeDesc type,
                string expected,
                CustomAttributeTypeNameFormatter formatter)
            {
                if (type is ByRefType byRef)
                    return expected.EndsWith('&') && TypeNameMatches(byRef.ParameterType, expected[..^1], formatter);
                if (type is PointerType pointer)
                    return expected.EndsWith('*') && TypeNameMatches(pointer.ParameterType, expected[..^1], formatter);
                if (type is ArrayType array)
                {
                    string suffix = array.IsSzArray ? "[]" : "[" + new string(',', array.Rank - 1) + "]";
                    return expected.EndsWith(suffix, StringComparison.Ordinal)
                        && TypeNameMatches(array.ElementType, expected[..^suffix.Length], formatter);
                }

                if (type.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true))
                    return expected.All(c => char.IsLetterOrDigit(c) || c is '_' or '`');

                return formatter.FormatName(type, false).Equals(expected, StringComparison.Ordinal);
            }
        }

        protected override string GetName(NodeFactory factory)
            => "Reflection dependency attributes on " + _entity.GetDisplayName();

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
