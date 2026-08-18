// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using ILCompiler.DependencyAnalysisFramework;

using ILLink.Shared;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using AssemblyAction = Mono.Linker.AssemblyAction;
using AssemblyRootMode = Mono.Linker.AssemblyRootMode;

namespace ILCompiler.DependencyAnalysis
{
    internal class AssemblyRootNode : DependencyNodeCore<NodeFactory>
    {
        private readonly string _assemblyName;
        private readonly AssemblyRootMode _mode;

        public AssemblyRootNode(string assemblyName, AssemblyRootMode mode)
            => (_assemblyName, _mode) = (assemblyName, mode);

        public void ApplyConfiguration(Mono.Linker.LinkContext settings, ILTrimTypeSystemContext typeSystemContext)
        {
            if (_mode != AssemblyRootMode.Library)
                return;

            var module = typeSystemContext.ResolveAssembly(
                AssemblyNameInfo.Parse(_assemblyName),
                throwIfNotFound: false) as EcmaModule;
            if (module is null)
                return;

            settings.Optimizations.Disable(
                Mono.Linker.CodeOptimizations.Sealer |
                Mono.Linker.CodeOptimizations.UnusedTypeChecks |
                Mono.Linker.CodeOptimizations.UnreachableBodies |
                Mono.Linker.CodeOptimizations.UnusedInterfaces |
                Mono.Linker.CodeOptimizations.RemoveDescriptors |
                Mono.Linker.CodeOptimizations.RemoveLinkAttributes |
                Mono.Linker.CodeOptimizations.RemoveSubstitutions |
                Mono.Linker.CodeOptimizations.RemoveDynamicDependencyAttribute |
                Mono.Linker.CodeOptimizations.OptimizeTypeHierarchyAnnotations |
                Mono.Linker.CodeOptimizations.SubstituteFeatureGuards,
                module.Assembly.GetName().Name);
            settings.DisableEventSourceSpecialHandling = false;
            settings.MetadataTrimming = Mono.Linker.MetadataTrimming.None;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            var module = factory.TypeSystemContext.ResolveAssembly(
                AssemblyNameInfo.Parse(_assemblyName),
                throwIfNotFound: false) as EcmaModule;
            if (module is null)
            {
                factory.Settings.LogError(null, DiagnosticId.RootAssemblyCouldNotBeFound, _assemblyName);
                yield break;
            }

            AssemblyAction action = factory.Settings.CalculateAssemblyAction(module);
            switch (action)
            {
                case AssemblyAction.Copy:
                    foreach (DependencyListEntry dependency in RootMembers(factory, module, RootMemberMode.All))
                        yield return dependency;
                    yield break;
                case AssemblyAction.CopyUsed:
                case AssemblyAction.Link:
                    break;
                default:
                    factory.Settings.LogError(
                        null,
                        DiagnosticId.RootAssemblyCannotUseAction,
                        module.Assembly.GetName().ToString(),
                        action.ToString());
                    yield break;
            }

            switch (_mode)
            {
                case AssemblyRootMode.AllMembers:
                    foreach (DependencyListEntry dependency in RootMembers(factory, module, RootMemberMode.All))
                        yield return dependency;
                    break;
                case AssemblyRootMode.EntryPoint:
                    int entryPointToken = module.PEReader.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress;
                    if (entryPointToken == 0)
                    {
                        factory.Settings.LogError(
                            null,
                            DiagnosticId.RootAssemblyDoesNotHaveEntryPoint,
                            module.Assembly.GetName().ToString());
                        yield break;
                    }

                    MethodDefinitionHandle entrypointToken = (MethodDefinitionHandle)MetadataTokens.Handle(entryPointToken);
                    yield return new DependencyListEntry(factory.MethodDefinition(module, entrypointToken), "Entrypoint");
                    yield return new DependencyListEntry(factory.MethodBody(module, entrypointToken), "Entrypoint body");
                    break;
                case AssemblyRootMode.VisibleMembers:
                    foreach (DependencyListEntry dependency in RootMembers(factory, module, RootMemberMode.Visible))
                        yield return dependency;
                    break;
                case AssemblyRootMode.Library:
                    foreach (DependencyListEntry dependency in RootMembers(factory, module, RootMemberMode.Library))
                        yield return dependency;
                    break;
            }
        }

        private static IEnumerable<DependencyListEntry> RootMembers(
            NodeFactory factory,
            EcmaModule module,
            RootMemberMode mode)
        {
            MetadataReader reader = module.MetadataReader;
            bool includeInternals = HasInternalsVisibleTo(module);

            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                TypeDefinition type = reader.GetTypeDefinition(typeHandle);
                bool isInterface = (type.Attributes & TypeAttributes.Interface) != 0;
                bool rootType = mode == RootMemberMode.All
                    || IsTypeVisible(reader, typeHandle, includeInternals)
                    || (mode == RootMemberMode.Library && isInterface);
                if (!rootType)
                    continue;

                yield return new DependencyListEntry(factory.TypeDefinition(module, typeHandle), "Assembly root type");

                foreach (InterfaceImplementationHandle interfaceHandle in type.GetInterfaceImplementations())
                {
                    InterfaceImplementation implementation = reader.GetInterfaceImplementation(interfaceHandle);
                    if (module.TryGetType(implementation.Interface)?.GetTypeDefinition() is EcmaType interfaceType)
                    {
                        yield return new DependencyListEntry(factory.InterfaceUse(interfaceType), "Assembly root interface");
                    }
                }

                foreach (MethodImplementationHandle methodImplementation in type.GetMethodImplementations())
                {
                    yield return new DependencyListEntry(
                        factory.MethodImplementation(module, methodImplementation),
                        "Assembly root method implementation");
                }

                foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
                {
                    MethodDefinition method = reader.GetMethodDefinition(methodHandle);
                    if (mode != RootMemberMode.All
                        && !(mode == RootMemberMode.Library && isInterface)
                        && !IsVisible(method.Attributes, includeInternals))
                    {
                        continue;
                    }

                    yield return new DependencyListEntry(factory.MethodDefinition(module, methodHandle), "Assembly root method");
                    yield return new DependencyListEntry(factory.MethodBody(module, methodHandle), "Assembly root method body");
                }

                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                    if (mode != RootMemberMode.All
                        && !(mode == RootMemberMode.Library && isInterface)
                        && !IsVisible(field.Attributes, includeInternals))
                    {
                        continue;
                    }

                    yield return new DependencyListEntry(factory.FieldDefinition(module, fieldHandle), "Assembly root field");
                }

                foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
                {
                    PropertyAccessors accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                    if (mode == RootMemberMode.All
                        || (mode == RootMemberMode.Library && isInterface)
                        || IsVisible(reader, accessors, includeInternals))
                    {
                        yield return new DependencyListEntry(factory.PropertyDefinition(module, propertyHandle), "Assembly root property");
                    }
                }

                foreach (EventDefinitionHandle eventHandle in type.GetEvents())
                {
                    EventAccessors accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                    if (mode == RootMemberMode.All
                        || (mode == RootMemberMode.Library && isInterface)
                        || IsVisible(reader, accessors, includeInternals))
                    {
                        yield return new DependencyListEntry(factory.EventDefinition(module, eventHandle), "Assembly root event");
                    }
                }
            }
        }

        private static bool HasInternalsVisibleTo(EcmaModule module)
        {
            MetadataReader reader = module.MetadataReader;
            foreach (CustomAttributeHandle attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                if (module.TryGetMethod(attribute.Constructor)?.OwningType is MetadataType attributeType
                    && attributeType.Namespace == "System.Runtime.CompilerServices"u8
                    && attributeType.Name == "InternalsVisibleToAttribute"u8)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTypeVisible(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            bool includeInternals)
        {
            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            TypeAttributes visibility = type.Attributes & TypeAttributes.VisibilityMask;
            bool visible = visibility is TypeAttributes.Public
                or TypeAttributes.NestedPublic
                or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamORAssem;

            if (!visible && includeInternals)
            {
                visible = visibility is TypeAttributes.NotPublic
                    or TypeAttributes.NestedAssembly
                    or TypeAttributes.NestedFamANDAssem;
            }

            return visible
                && (!type.IsNested || IsTypeVisible(reader, type.GetDeclaringType(), includeInternals));
        }

        private static bool IsVisible(MethodAttributes attributes, bool includeInternals)
        {
            MethodAttributes visibility = attributes & MethodAttributes.MemberAccessMask;
            return visibility is MethodAttributes.Public
                or MethodAttributes.Family
                or MethodAttributes.FamORAssem
                || (includeInternals && visibility is MethodAttributes.Assembly or MethodAttributes.FamANDAssem);
        }

        private static bool IsVisible(FieldAttributes attributes, bool includeInternals)
        {
            FieldAttributes visibility = attributes & FieldAttributes.FieldAccessMask;
            return visibility is FieldAttributes.Public
                or FieldAttributes.Family
                or FieldAttributes.FamORAssem
                || (includeInternals && visibility is FieldAttributes.Assembly or FieldAttributes.FamANDAssem);
        }

        private static bool IsVisible(
            MetadataReader reader,
            PropertyAccessors accessors,
            bool includeInternals)
        {
            return (!accessors.Getter.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Getter).Attributes, includeInternals))
                || (!accessors.Setter.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Setter).Attributes, includeInternals));
        }

        private static bool IsVisible(
            MetadataReader reader,
            EventAccessors accessors,
            bool includeInternals)
        {
            return (!accessors.Adder.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Adder).Attributes, includeInternals))
                || (!accessors.Remover.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Remover).Attributes, includeInternals))
                || (!accessors.Raiser.IsNil && IsVisible(reader.GetMethodDefinition(accessors.Raiser).Attributes, includeInternals));
        }

        private enum RootMemberMode
        {
            All,
            Visible,
            Library,
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => _mode == AssemblyRootMode.Library;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
        {
            if (_mode != AssemblyRootMode.Library)
                yield break;

            var module = factory.TypeSystemContext.ResolveAssembly(
                AssemblyNameInfo.Parse(_assemblyName),
                throwIfNotFound: false) as EcmaModule;
            if (module is null || factory.Settings.CalculateAssemblyAction(module) != AssemblyAction.Link)
                yield break;

            MetadataReader reader = module.MetadataReader;
            foreach (TypeDefinitionHandle typeHandle in reader.TypeDefinitions)
            {
                foreach (MethodDefinitionHandle methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                {
                    if (!IsLibraryPreservedMethod(module, methodHandle))
                        continue;

                    DependencyNodeCore<NodeFactory> condition = factory.TypeDefinition(module, typeHandle);
                    yield return new CombinedDependencyListEntry(
                        factory.MethodDefinition(module, methodHandle),
                        condition,
                        "Library serialization method");
                    yield return new CombinedDependencyListEntry(
                        factory.MethodBody(module, methodHandle),
                        condition,
                        "Library serialization method body");
                }
            }
        }

        private static bool IsLibraryPreservedMethod(EcmaModule module, MethodDefinitionHandle methodHandle)
        {
            EcmaMethod method = (EcmaMethod)module.GetMethod(methodHandle);
            if (method.IsConstructor
                && method.Signature.Length == 2
                && IsType(method.Signature[0], "System.Runtime.Serialization"u8, "SerializationInfo"u8)
                && IsType(method.Signature[1], "System.Runtime.Serialization"u8, "StreamingContext"u8))
            {
                return true;
            }

            MetadataReader reader = module.MetadataReader;
            foreach (CustomAttributeHandle attributeHandle in reader.GetMethodDefinition(methodHandle).GetCustomAttributes())
            {
                CustomAttribute attribute = reader.GetCustomAttribute(attributeHandle);
                if (module.TryGetMethod(attribute.Constructor)?.OwningType is not MetadataType attributeType
                    || attributeType.Namespace != "System.Runtime.Serialization"u8)
                {
                    continue;
                }

                if (attributeType.Name == "OnSerializingAttribute"u8
                    || attributeType.Name == "OnSerializedAttribute"u8
                    || attributeType.Name == "OnDeserializingAttribute"u8
                    || attributeType.Name == "OnDeserializedAttribute"u8)
                {
                    return true;
                }
            }

            return false;

            static bool IsType(TypeDesc type, ReadOnlySpan<byte> @namespace, ReadOnlySpan<byte> name)
            {
                MetadataType definition = type.GetTypeDefinition() as MetadataType;
                return definition is not null
                    && definition.Namespace == @namespace
                    && definition.Name == name;
            }
        }

        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory context) => null;
        protected override string GetName(NodeFactory context) => $"Assembly root: {_assemblyName} ({_mode})";
    }
}
