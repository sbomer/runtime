// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Mono.Linker;

using Internal.TypeSystem.Ecma;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents an entry in the Type Reference metadata table.
    /// </summary>
    public sealed class TypeReferenceNode : TokenBasedNode
    {
        public TypeReferenceNode(EcmaModule module, TypeReferenceHandle handle)
            : base(module, handle)
        {
        }

        private TypeReferenceHandle Handle => (TypeReferenceHandle)_handle;

        TokenWriterNode GetResolutionScopeNode(NodeFactory factory)
        {
            TypeReference typeRef = _module.MetadataReader.GetTypeReference(Handle);

            if (typeRef.ResolutionScope.Kind == HandleKind.AssemblyReference)
            {
                // Resolve to an EcmaType to go through any forwarders.
                var ecmaType = (EcmaType)_module.GetObject(Handle);
                EcmaAssembly referencedAssembly = (EcmaAssembly)ecmaType.Module;
                return factory.AssemblyReference(_module, referencedAssembly);
            }
            else
            {
                return typeRef.ResolutionScope.Kind switch
                {
                    HandleKind.TypeReference => factory.TypeReference(_module, (TypeReferenceHandle)typeRef.ResolutionScope),
                    HandleKind.ModuleReference => factory.ModuleReference(_module, (ModuleReferenceHandle)typeRef.ResolutionScope),
                    _ => throw new InvalidOperationException(typeRef.ResolutionScope.Kind.ToString()),
                };
            }
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            yield return new(GetResolutionScopeNode(factory), "Resolution Scope of a type reference");

            TypeReference typeReference = _module.MetadataReader.GetTypeReference(Handle);
            var typeDescObject = _module.GetObject(Handle);
            if (typeDescObject is not EcmaType typeDef)
                yield break;

            AssemblyAction targetAction = factory.Settings.CalculateAssemblyAction(typeDef.Module);
            if (targetAction == AssemblyAction.Link)
            {
                yield return new(factory.TypeDefinition(typeDef.Module, typeDef.Handle), "Target of a type reference");
            }
            else if (targetAction == AssemblyAction.CopyUsed)
            {
                yield return new(factory.CopyUsedAssembly(typeDef.Module), "CopyUsed target of a type reference");
            }

            AssemblyAction sourceAction = factory.Settings.CalculateAssemblyAction(_module);
            if (sourceAction == AssemblyAction.Copy &&
                typeReference.ResolutionScope.Kind == HandleKind.AssemblyReference &&
                _module.GetObject(typeReference.ResolutionScope) is EcmaModule resolutionModule &&
                resolutionModule != typeDef.Module)
            {
                MetadataReader resolutionReader = resolutionModule.MetadataReader;
                foreach (ExportedTypeHandle exportedTypeHandle in resolutionReader.ExportedTypes)
                {
                    ExportedType exportedType = resolutionReader.GetExportedType(exportedTypeHandle);
                    if (exportedType.IsForwarder &&
                        resolutionReader.StringComparer.Equals(exportedType.Namespace, _module.MetadataReader.GetString(typeReference.Namespace)) &&
                        resolutionReader.StringComparer.Equals(exportedType.Name, _module.MetadataReader.GetString(typeReference.Name)))
                    {
                        yield return new(
                            factory.ExportedType(resolutionModule, exportedTypeHandle),
                            "Type forwarder of a type reference");
                        break;
                    }
                }
            }
        }

        protected override EntityHandle WriteInternal(ModuleWritingContext writeContext)
        {
            MetadataReader reader = _module.MetadataReader;
            TypeReference typeRef = reader.GetTypeReference(Handle);

            var builder = writeContext.MetadataBuilder;
            TokenWriterNode resolutionScopeNode = GetResolutionScopeNode(writeContext.Factory);
            EntityHandle targetResolutionScopeToken;
            if (resolutionScopeNode is AssemblyReferenceNode assemblyRefNode)
            {
                Debug.Assert(assemblyRefNode.TargetToken.HasValue);
                targetResolutionScopeToken = (EntityHandle)assemblyRefNode.TargetToken.Value;
            }
            else
            {
                targetResolutionScopeToken = writeContext.TokenMap.MapToken(typeRef.ResolutionScope);
            }

            return builder.AddTypeReference(targetResolutionScopeToken,
                builder.GetOrAddString(reader.GetString(typeRef.Namespace)),
                builder.GetOrAddString(reader.GetString(typeRef.Name)));
        }

        public override string ToString()
        {
            MetadataReader reader = _module.MetadataReader;
            return reader.GetString(reader.GetTypeReference(Handle).Name);
        }
    }
}
