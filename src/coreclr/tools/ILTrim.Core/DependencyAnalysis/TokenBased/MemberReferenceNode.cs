// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using ILCompiler.DependencyAnalysisFramework;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents an entry in the MemberRef metadata table.
    /// </summary>
    public sealed class MemberReferenceNode : TokenBasedNode
    {
        public MemberReferenceNode(EcmaModule module, MemberReferenceHandle handle)
            : base(module, handle)
        {
        }

        private MemberReferenceHandle Handle => (MemberReferenceHandle)_handle;

        public MethodDesc? Method => _module.GetObject(Handle) as MethodDesc;

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            var methodOrFieldDef = _module.GetObject(Handle);
            MemberReference memberRef = _module.MetadataReader.GetMemberReference(Handle);

            DependencyList dependencies = new DependencyList();

            switch (methodOrFieldDef)
            {
                case MethodDesc method:
                    if (method.GetTypicalMethodDefinition() is EcmaMethod ecmaMethod)
                    {
                        AddTargetDependency(
                            dependencies,
                            factory,
                            ecmaMethod.Module,
                            factory.MethodDefinition(ecmaMethod.Module, ecmaMethod.Handle),
                            "Target method def of member reference");
                    }
                    break;

                case FieldDesc field:
                    var ecmaField = (EcmaField)field.GetTypicalFieldDefinition();
                    AddTargetDependency(
                        dependencies,
                        factory,
                        ecmaField.Module,
                        factory.FieldDefinition(ecmaField.Module, ecmaField.Handle),
                        "Target field def of member reference");
                    break;
            }

            if (!memberRef.Parent.IsNil)
            {
                switch (memberRef.Parent.Kind)
                {
                    case HandleKind.TypeDefinition:
                    case HandleKind.TypeReference:
                    case HandleKind.TypeSpecification:
                        dependencies.Add(factory.GetNodeForTypeToken(_module, memberRef.Parent), "Parent of member reference");
                        break;
                    case HandleKind.MethodDefinition:
                        dependencies.Add(factory.MethodDefinition(_module, (MethodDefinitionHandle)memberRef.Parent), "Parent of member reference");
                        break;
                    case HandleKind.ModuleReference:
                        dependencies.Add(factory.ModuleReference(_module, (ModuleReferenceHandle)memberRef.Parent), "Parent of member reference");
                        break;
                    default:
                        throw new InvalidOperationException(memberRef.Parent.Kind.ToString());
                }
            }

            BlobReader signatureBlob = _module.MetadataReader.GetBlobReader(memberRef.Signature);
            EcmaSignatureAnalyzer.AnalyzeMemberReferenceSignature(
                _module,
                signatureBlob,
                factory,
                dependencies);

            return dependencies;

            static void AddTargetDependency(
                DependencyList dependencies,
                NodeFactory factory,
                EcmaModule targetModule,
                DependencyNodeCore<NodeFactory> targetDefinition,
                string reason)
            {
                switch (factory.Settings.CalculateAssemblyAction(targetModule))
                {
                    case Mono.Linker.AssemblyAction.Link:
                        dependencies.Add(targetDefinition, reason);
                        break;
                    case Mono.Linker.AssemblyAction.CopyUsed:
                        dependencies.Add(factory.CopyUsedAssembly(targetModule), "Used copy assembly");
                        break;
                }
            }
        }

        protected override EntityHandle WriteInternal(ModuleWritingContext writeContext)
        {
            MetadataReader reader = _module.MetadataReader;
            MemberReference memberRef = reader.GetMemberReference(Handle);

            var builder = writeContext.MetadataBuilder;

            var signatureBlob = writeContext.GetSharedBlobBuilder();
            EcmaSignatureRewriter.RewriteMemberReferenceSignature(
                reader.GetBlobReader(memberRef.Signature),
                writeContext.TokenMap,
                signatureBlob);

            return builder.AddMemberReference(writeContext.TokenMap.MapToken(memberRef.Parent),
                builder.GetOrAddString(reader.GetString(memberRef.Name)),
                builder.GetOrAddBlob(signatureBlob));
        }

        public override string ToString()
        {
            MetadataReader reader = _module.MetadataReader;
            return reader.GetString(reader.GetMemberReference(Handle).Name);
        }
    }
}
