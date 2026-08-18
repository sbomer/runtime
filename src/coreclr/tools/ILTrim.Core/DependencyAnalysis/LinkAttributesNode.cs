// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Xml.XPath;

using ILCompiler.Dataflow;
using ILCompiler.DependencyAnalysisFramework;
using ILCompiler.Logging;

using ILLink.Shared;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class LinkAttributesNode : DependencyNodeCore<NodeFactory>
    {
        private readonly string _filePath;
        private List<CombinedDependencyListEntry> _dependencies;

        public LinkAttributesNode(string filePath)
        {
            _filePath = filePath;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory) => null;

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
        {
            if (_dependencies is null)
            {
                using FileStream stream = File.OpenRead(_filePath);
                var parser = new DynamicDependencyLinkAttributesParser(
                    factory.Logger,
                    factory,
                    stream,
                    _filePath,
                    factory.Settings.FeatureSettings);
                _dependencies = parser.GetDependencies();
            }

            return _dependencies;
        }

        protected override string GetName(NodeFactory factory) => "Link attributes: " + _filePath;

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => true;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(
            List<DependencyNodeCore<NodeFactory>> markedNodes,
            int firstNode,
            NodeFactory factory) => null;

        private sealed class DynamicDependencyLinkAttributesParser : ProcessLinkerXmlBase
        {
            private const string DynamicDependencyAttributeName =
                "System.Diagnostics.CodeAnalysis.DynamicDependencyAttribute";

            private readonly NodeFactory _factory;
            private readonly List<CombinedDependencyListEntry> _dependencies = new();

            public DynamicDependencyLinkAttributesParser(
                Logger logger,
                NodeFactory factory,
                Stream documentStream,
                string xmlDocumentLocation,
                IReadOnlyDictionary<string, bool> featureSwitchValues)
                : base(
                    logger,
                    factory.TypeSystemContext,
                    documentStream,
                    xmlDocumentLocation,
                    featureSwitchValues)
            {
                _factory = factory;
            }

            public List<CombinedDependencyListEntry> GetDependencies()
            {
                ProcessXml(ignoreResource: false);
                return _dependencies;
            }

            protected override AllowedAssemblies AllowedAssemblySelector => AllowedAssemblies.AnyAssembly;

            protected override void ProcessAssembly(ModuleDesc assembly, XPathNavigator nav, bool warnOnUnresolvedTypes)
                => ProcessTypes(assembly, nav, warnOnUnresolvedTypes);

            protected override void ProcessType(TypeDesc type, XPathNavigator nav)
                => ProcessTypeChildren(type, nav);

            protected override void ProcessField(TypeDesc type, FieldDesc field, XPathNavigator nav)
                => ProcessAttributes(type, field, nav);

            protected override void ProcessMethod(TypeDesc type, MethodDesc method, XPathNavigator nav, object customData)
                => ProcessAttributes(type, method, nav);

            protected override MethodDesc GetMethod(TypeDesc type, string signature)
            {
                foreach (MethodDesc method in type.GetAllMethods())
                {
                    if (signature == GetMethodSignature(method, false))
                        return method;
                }

                return null;
            }

            private void ProcessAttributes(TypeDesc owningType, TypeSystemEntity provider, XPathNavigator nav)
            {
                foreach (XPathNavigator attributeNav in nav.SelectChildren("attribute", XmlNamespace))
                {
                    if (!ShouldProcessElement(attributeNav)
                        || GetFullName(attributeNav) != DynamicDependencyAttributeName)
                    {
                        continue;
                    }

                    XPathNodeIterator argumentIterator = attributeNav.SelectChildren("argument", XmlNamespace);
                    var arguments = new List<(string Type, string Value)>();
                    foreach (XPathNavigator argumentNav in argumentIterator)
                    {
                        arguments.Add((
                            GetAttribute(argumentNav, "type"),
                            argumentNav.Value));
                    }

                    if (!TryResolveTarget(
                            owningType,
                            arguments,
                            out MetadataType targetType,
                            out object memberSelector,
                            out ModuleDesc typeNameModule))
                    {
                        continue;
                    }

                    var marker = new ReflectionMarker(
                        _factory.Logger,
                        _factory,
                        _factory.FlowAnnotations,
                        typeHierarchyDataFlowOrigin: null,
                        enabled: true);
                    IEnumerable<TypeSystemEntity> members = memberSelector switch
                    {
                        string memberSignature => DocumentationSignatureParser.GetMembersByDocumentationSignature(
                            targetType,
                            memberSignature,
                            acceptName: true),
                        DynamicallyAccessedMemberTypes memberTypes => targetType.GetDynamicallyAccessedMembers(memberTypes),
                        _ => Array.Empty<TypeSystemEntity>(),
                    };

                    foreach (TypeSystemEntity member in members.Distinct())
                    {
                        marker.MarkTypeSystemEntity(
                            new MessageOrigin(provider),
                            member,
                            "DynamicDependencyAttribute from link attributes");
                    }

                    DependencyNodeCore<NodeFactory> condition = GetCondition(provider);
                    foreach (DependencyListEntry dependency in marker.Dependencies)
                    {
                        _dependencies.Add(new CombinedDependencyListEntry(
                            dependency.Node,
                            condition,
                            dependency.Reason));
                    }

                    if (typeNameModule is not null)
                    {
                        _dependencies.Add(new CombinedDependencyListEntry(
                            _factory.ReflectionVisibleModule(typeNameModule),
                            condition,
                            "DynamicDependencyAttribute type name from link attributes"));
                    }
                }
            }

            private bool TryResolveTarget(
                TypeDesc owningType,
                List<(string Type, string Value)> arguments,
                [NotNullWhen(true)] out MetadataType targetType,
                [NotNullWhen(true)] out object memberSelector,
                out ModuleDesc typeNameModule)
            {
                targetType = null;
                memberSelector = null;
                typeNameModule = null;

                if (arguments.Count == 0)
                    return false;

                (string firstArgumentType, string firstArgumentValue) = arguments[0];
                if (firstArgumentType.EndsWith(
                        nameof(DynamicallyAccessedMemberTypes),
                        StringComparison.Ordinal)
                    && Enum.TryParse(
                        firstArgumentValue,
                        ignoreCase: true,
                        out DynamicallyAccessedMemberTypes memberTypes))
                {
                    memberSelector = memberTypes;
                }
                else
                {
                    memberSelector = firstArgumentValue;
                }

                TypeDesc resolvedType;
                switch (arguments.Count)
                {
                    case 1 when memberSelector is string:
                        resolvedType = owningType;
                        break;

                    case 2 when arguments[1].Type.EndsWith("System.Type", StringComparison.Ordinal):
                        resolvedType = ResolveCustomAttributeType(arguments[1].Value, owningType);
                        break;

                    case 3:
                        typeNameModule = ResolveAssembly(arguments[2].Value);
                        resolvedType = typeNameModule is null
                            ? null
                            : DocumentationSignatureParser.GetTypeByDocumentationSignature(
                                (IAssemblyDesc)typeNameModule,
                                arguments[1].Value);
                        break;

                    default:
                        return false;
                }

                while (resolvedType?.IsParameterizedType == true)
                    resolvedType = ((ParameterizedType)resolvedType).ParameterType;

                targetType = resolvedType?.GetTypeDefinition() as MetadataType;
                return targetType is not null;
            }

            private TypeDesc ResolveCustomAttributeType(string typeName, TypeDesc owningType)
            {
                ModuleDesc module = ((MetadataType)owningType.GetTypeDefinition()).Module;
                int assemblySeparator = typeName.LastIndexOf(',');
                if (assemblySeparator >= 0)
                {
                    module = ResolveAssembly(typeName[(assemblySeparator + 1)..].Trim()) ?? module;
                    typeName = typeName[..assemblySeparator];
                }

                return CecilCompatibleTypeParser.GetType(module, typeName);
            }

            private ModuleDesc ResolveAssembly(string assemblyName)
            {
                try
                {
                    return _factory.TypeSystemContext.ResolveAssembly(
                        AssemblyNameInfo.Parse(assemblyName),
                        throwIfNotFound: false);
                }
                catch (TypeSystemException)
                {
                    return null;
                }
            }

            private DependencyNodeCore<NodeFactory> GetCondition(TypeSystemEntity provider)
                => provider switch
                {
                    EcmaMethod method => _factory.MethodDefinition(method.Module, method.Handle),
                    EcmaField field => _factory.FieldDefinition(field.Module, field.Handle),
                    _ => throw new InvalidOperationException(),
                };
        }
    }
}
