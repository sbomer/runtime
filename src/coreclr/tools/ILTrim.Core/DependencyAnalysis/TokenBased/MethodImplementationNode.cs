// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection.Metadata;

using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a row in the MethodImpl table.
    /// </summary>
    public sealed class MethodImplementationNode : TokenBasedNode
    {
        public MethodImplementationNode(EcmaModule module, MethodImplementationHandle handle)
            : base(module, handle)
        {
        }

        private MethodImplementationHandle Handle => (MethodImplementationHandle)_handle;

        internal static MethodImplementationHandle TryGetMethodImplementationHandle(
            EcmaType implementingType,
            MethodDesc declarationMethod,
            MethodDesc bodyMethod)
        {
            MetadataReader reader = implementingType.MetadataReader;
            MethodImplementationHandle matchingBodyHandle = default;

            foreach (MethodImplementationHandle implementationHandle in reader.GetTypeDefinition(implementingType.Handle).GetMethodImplementations())
            {
                MethodImplementation implementation = reader.GetMethodImplementation(implementationHandle);
                MethodDesc declaration = implementingType.Module.TryGetMethod(implementation.MethodDeclaration);
                MethodDesc body = implementingType.Module.TryGetMethod(implementation.MethodBody);
                if (declaration == declarationMethod && body == bodyMethod)
                    return implementationHandle;

                if (body?.GetTypicalMethodDefinition() == bodyMethod.GetTypicalMethodDefinition() &&
                    declaration?.GetTypicalMethodDefinition() == declarationMethod.GetTypicalMethodDefinition())
                {
                    matchingBodyHandle = implementationHandle;
                }
            }

            return matchingBodyHandle;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            var methodImpl = _module.MetadataReader.GetMethodImplementation(Handle);
            yield return new(factory.GetNodeForMethodToken(_module, methodImpl.MethodBody), "MethodImpl body");
            yield return new(factory.GetNodeForMethodToken(_module, methodImpl.MethodDeclaration), "MethodImpl decl");
            yield return new(factory.GetNodeForTypeToken(_module, methodImpl.Type), "MethodImpl type");

            MethodDesc declaration = _module.TryGetMethod(methodImpl.MethodDeclaration);
            if (declaration?.OwningType is DefType interfaceType && interfaceType.IsInterface)
            {
                EcmaType implementingType = (EcmaType)_module.GetObject(methodImpl.Type);
                DefType interfaceToMark = interfaceType;
                TypeDefinition implementingTypeDefinition = _module.MetadataReader.GetTypeDefinition(implementingType.Handle);
                var directInterfaces = new List<DefType>();

                foreach (InterfaceImplementationHandle interfaceImplementationHandle in implementingTypeDefinition.GetInterfaceImplementations())
                {
                    InterfaceImplementation interfaceImplementation = _module.MetadataReader.GetInterfaceImplementation(interfaceImplementationHandle);
                    if (_module.TryGetType(interfaceImplementation.Interface) is DefType directInterface)
                        directInterfaces.Add(directInterface);
                }

                bool directlyImplemented = false;
                foreach (DefType directInterface in directInterfaces)
                {
                    if (directInterface == interfaceType)
                    {
                        directlyImplemented = true;
                        break;
                    }
                }

                if (!directlyImplemented)
                {
                    foreach (DefType directInterface in directInterfaces)
                    {
                        if (directInterface.CanCastTo(interfaceType))
                        {
                            interfaceToMark = directInterface;
                            break;
                        }
                    }
                }

                EcmaType interfaceDefinition = (EcmaType)interfaceToMark.GetTypeDefinition();
                if (declaration.Signature.IsStatic)
                {
                    yield return new(
                        factory.InterfaceImplementationUse(implementingType, interfaceDefinition),
                        "Static MethodImpl interface");
                }
                else
                {
                    yield return new(
                        factory.InterfaceUse(interfaceDefinition),
                        "MethodImpl interface");
                }
            }
        }

        public override string ToString()
        {
            return "MethodImpl";
        }

        protected override EntityHandle WriteInternal(ModuleWritingContext writeContext)
        {
            var methodImpl = _module.MetadataReader.GetMethodImplementation(Handle);
            return writeContext.MetadataBuilder.AddMethodImplementation(
                (TypeDefinitionHandle)writeContext.TokenMap.MapToken(methodImpl.Type),
                writeContext.TokenMap.MapToken(methodImpl.MethodBody),
                writeContext.TokenMap.MapToken(methodImpl.MethodDeclaration));
        }
    }
}
