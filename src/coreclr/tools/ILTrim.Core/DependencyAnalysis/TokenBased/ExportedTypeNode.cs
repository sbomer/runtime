// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    public sealed class ExportedTypeNode : TokenBasedNode
    {
        public ExportedTypeNode(EcmaModule module, ExportedTypeHandle handle)
            : base(module, handle)
        {
        }

        private ExportedTypeHandle Handle => (ExportedTypeHandle)_handle;

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            ExportedType exportedType = _module.MetadataReader.GetExportedType(Handle);
            var dependencies = new DependencyList
            {
                new DependencyListEntry(factory.ModuleDefinition(_module), "Owning module"),
            };

            switch (exportedType.Implementation.Kind)
            {
                case HandleKind.AssemblyReference:
                    dependencies.Add(
                        factory.AssemblyReference(
                            _module,
                            (EcmaAssembly)_module.GetObject(exportedType.Implementation)),
                        "Type forwarder assembly");
                    break;
                case HandleKind.ExportedType:
                    dependencies.Add(
                        factory.ExportedType(_module, (ExportedTypeHandle)exportedType.Implementation),
                        "Declaring exported type");
                    break;
            }

            CustomAttributeNode.AddDependenciesDueToCustomAttributes(
                ref dependencies,
                factory,
                _module,
                exportedType.GetCustomAttributes());
            return dependencies;
        }

        protected override EntityHandle WriteInternal(ModuleWritingContext writeContext)
        {
            MetadataReader reader = _module.MetadataReader;
            ExportedType exportedType = reader.GetExportedType(Handle);
            EntityHandle implementation = exportedType.Implementation;
            if (implementation.Kind == HandleKind.AssemblyReference)
            {
                AssemblyReferenceNode assemblyReference = writeContext.Factory.AssemblyReference(
                    _module,
                    (EcmaAssembly)_module.GetObject(implementation));
                Debug.Assert(assemblyReference.TargetToken.HasValue);
                implementation = assemblyReference.TargetToken.Value;
            }
            else
            {
                implementation = writeContext.TokenMap.MapToken(implementation);
            }

            return writeContext.MetadataBuilder.AddExportedType(
                exportedType.Attributes,
                writeContext.MetadataBuilder.GetOrAddString(reader.GetString(exportedType.Namespace)),
                writeContext.MetadataBuilder.GetOrAddString(reader.GetString(exportedType.Name)),
                implementation,
                exportedType.GetTypeDefinitionId());
        }

        public override string ToString()
        {
            MetadataReader reader = _module.MetadataReader;
            ExportedType exportedType = reader.GetExportedType(Handle);
            return reader.GetString(exportedType.Name);
        }
    }
}
