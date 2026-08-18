// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using Internal.IL;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.DependencyAnalysisFramework;

using ILCompiler.Dataflow;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents method body bytes emitted into the executable.
    /// </summary>
    public class MethodBodyNode : DependencyNodeCore<NodeFactory>, INodeWithDeferredDependencies
    {
        private readonly EcmaModule _module;
        private readonly MethodDefinitionHandle _methodHandle;
        DependencyList _dependencies = null;

        public MethodBodyNode(EcmaModule module, MethodDefinitionHandle methodHandle)
        {
            _module = module;
            _methodHandle = methodHandle;
        }

        public override bool StaticDependenciesAreComputed => _dependencies != null;

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory context) => _dependencies;

        void INodeWithDeferredDependencies.ComputeDependencies(NodeFactory factory)
        {
            _dependencies = new DependencyList();

            // RVA = 0 is an extern method, such as a DllImport
            int rva = _module.MetadataReader.GetMethodDefinition(_methodHandle).RelativeVirtualAddress;
            if (rva == 0)
                return;

            MethodBodyBlock bodyBlock = _module.PEReader.GetMethodBody(rva);
            EcmaMethod owningMethod = (EcmaMethod)_module.GetMethod(_methodHandle);
            MethodIL methodIL = GetMethodIL(factory, owningMethod);

            if (HasExactInstantiation(owningMethod))
                _dependencies.Add(factory.MethodInstantiation(owningMethod), "Method body analysis");

            if (methodIL.GetLocals().Length != 0 && !bodyBlock.LocalSignature.IsNil)
                _dependencies.Add(factory.StandaloneSignature(_module, bodyBlock.LocalSignature), "Signatures of local variables");

            ILExceptionRegion[] exceptionRegions = methodIL.GetExceptionRegions();
            if (exceptionRegions.Length != 0)
            {
                foreach (ILExceptionRegion exceptionRegion in exceptionRegions)
                {
                    if (exceptionRegion.Kind != ILExceptionRegionKind.Catch)
                        continue;

                    _dependencies.Add(
                        factory.GetNodeForTypeToken(_module, MetadataTokens.EntityHandle(exceptionRegion.ClassToken)),
                        "Catch type of exception region");
                }
            }

            bool requiresMethodBodyScanner = ReflectionMethodBodyScanner.RequiresReflectionMethodBodyScannerForMethodBody(
                factory.FlowAnnotations, _module.GetMethod(_methodHandle));

            ILReader ilReader = new(methodIL.GetILBytes());
            while (ilReader.HasNext)
            {
                ILOpcode opcode = ilReader.ReadILOpcode();

                switch (opcode)
                {
                    case ILOpcode.sizeof_:
                    case ILOpcode.newarr:
                    case ILOpcode.stsfld:
                    case ILOpcode.ldsfld:
                    case ILOpcode.ldsflda:
                    case ILOpcode.stfld:
                    case ILOpcode.ldfld:
                    case ILOpcode.ldflda:
                    case ILOpcode.call:
                    case ILOpcode.calli:
                    case ILOpcode.callvirt:
                    case ILOpcode.newobj:
                    case ILOpcode.ldtoken:
                    case ILOpcode.ldftn:
                    case ILOpcode.ldvirtftn:
                    case ILOpcode.initobj:
                    case ILOpcode.stelem:
                    case ILOpcode.ldelem:
                    case ILOpcode.ldelema:
                    case ILOpcode.box:
                    case ILOpcode.unbox:
                    case ILOpcode.unbox_any:
                    case ILOpcode.jmp:
                    case ILOpcode.cpobj:
                    case ILOpcode.ldobj:
                    case ILOpcode.castclass:
                    case ILOpcode.isinst:
                    case ILOpcode.stobj:
                    case ILOpcode.refanyval:
                    case ILOpcode.mkrefany:
                    case ILOpcode.constrained:
                        int tokenValue = ilReader.ReadILToken();
                        EntityHandle token = MetadataTokens.EntityHandle(tokenValue);

                        MethodDesc method;
                        if (opcode == ILOpcode.newobj || opcode == ILOpcode.call || opcode == ILOpcode.callvirt ||
                            opcode == ILOpcode.ldvirtftn || opcode == ILOpcode.ldftn)
                        {
                            method = methodIL.GetObject(tokenValue, NotFoundBehavior.ReturnNull) as MethodDesc;
                        }
                        else
                        {
                            method = null;
                        }

                        FieldDesc field;
                        if (opcode == ILOpcode.ldfld || opcode == ILOpcode.ldflda ||
                            opcode == ILOpcode.ldsfld || opcode == ILOpcode.ldsflda)
                        {
                            field = methodIL.GetObject(tokenValue, NotFoundBehavior.ReturnNull) as FieldDesc;
                        }
                        else
                        {
                            field = null;
                        }

                        if ((opcode == ILOpcode.callvirt || opcode == ILOpcode.ldvirtftn) &&
                            method != null && method.IsVirtual)
                        {
                            MethodDesc slotMethod = MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(
                                method.GetTypicalMethodDefinition());
                            _dependencies.Add(factory.VirtualMethodUse((EcmaMethod)slotMethod), "Callvirt/ldvirtftn");
                        }

                        if (method is not null && HasExactInstantiation(method))
                            _dependencies.Add(factory.MethodInstantiation(method), "Method call analysis");

                        _dependencies.Add(token.Kind switch
                        {
                            HandleKind.TypeDefinition => factory.TypeDefinition(_module, (TypeDefinitionHandle)token),
                            HandleKind.TypeReference => factory.TypeReference(_module, (TypeReferenceHandle)token),
                            HandleKind.TypeSpecification => factory.TypeSpecification(_module, (TypeSpecificationHandle)token),
                            HandleKind.MethodDefinition => factory.MethodDefinition(_module, (MethodDefinitionHandle)token),
                            HandleKind.FieldDefinition => factory.FieldDefinition(_module, (FieldDefinitionHandle)token),
                            HandleKind.MemberReference => factory.MemberReference(_module, (MemberReferenceHandle)token),
                            HandleKind.MethodSpecification => factory.MethodSpecification(_module, (MethodSpecificationHandle)token),
                            HandleKind.StandaloneSignature => factory.StandaloneSignature(_module, (StandaloneSignatureHandle)token),
                            _ => throw new InvalidOperationException(token.Kind.ToString()),
                        }, "Instruction operand");

                        if (method != null && !requiresMethodBodyScanner)
                        {
                            requiresMethodBodyScanner |= ReflectionMethodBodyScanner.RequiresReflectionMethodBodyScannerForCallSite(
                                factory.FlowAnnotations, method.GetTypicalMethodDefinition());
                        }
                        if (field != null && !requiresMethodBodyScanner)
                        {
                            requiresMethodBodyScanner |= ReflectionMethodBodyScanner.RequiresReflectionMethodBodyScannerForAccess(
                                factory.FlowAnnotations, field.GetTypicalFieldDefinition());
                        }

                        break;

                    default:
                        ilReader.Skip(opcode);
                        break;
                }
            }

            if (requiresMethodBodyScanner &&
                factory.TryGetDataflowAnalyzedMethod(owningMethod, out DataflowAnalyzedMethodNode analyzedMethod))
            {
                _dependencies.Add(analyzedMethod, "Method requires dataflow analysis");
            }

            static bool HasExactInstantiation(MethodDesc method)
            {
                if (method.GetTypicalMethodDefinition() is not EcmaMethod ||
                    method.OwningType.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true))
                {
                    return false;
                }

                foreach (TypeDesc argument in method.Instantiation)
                {
                    if (argument.ContainsSignatureVariables(treatGenericParameterLikeSignatureVariable: true))
                        return false;
                }

                return true;
            }
        }

        public int Write(ModuleWritingContext writeContext)
        {
            int rva = _module.MetadataReader.GetMethodDefinition(_methodHandle).RelativeVirtualAddress;
            if (rva == 0)
                return -1;

            MethodBodyBlock bodyBlock = _module.PEReader.GetMethodBody(rva);
            EcmaMethod owningMethod = (EcmaMethod)_module.GetMethod(_methodHandle);
            MethodIL methodIL = GetMethodIL(writeContext.Factory, owningMethod);
            ILExceptionRegion[] exceptionRegions = methodIL.GetExceptionRegions();

            // Use small exception regions when the code size of the try block and
            // the handler code are less than 256 bytes and offsets smaller than 65536 bytes.
            bool useSmallExceptionRegions = ExceptionRegionEncoder.IsSmallRegionCount(exceptionRegions.Length);
            if (useSmallExceptionRegions)
            {
                foreach (ILExceptionRegion exceptionRegion in exceptionRegions)
                {
                    if (!ExceptionRegionEncoder.IsSmallExceptionRegion(exceptionRegion.TryOffset, exceptionRegion.TryLength) ||
                        !ExceptionRegionEncoder.IsSmallExceptionRegion(exceptionRegion.HandlerOffset, exceptionRegion.HandlerLength))
                    {
                        useSmallExceptionRegions = false;
                        break;
                    }
                }
            }

            BlobBuilder outputBodyBuilder = writeContext.GetSharedBlobBuilder();
            byte[] bodyBytes = methodIL.GetILBytes();
            ILReader ilReader = new ILReader(bodyBytes);
            while (ilReader.HasNext)
            {
                int offset = ilReader.Offset;
                ILOpcode opcode = ilReader.ReadILOpcode();

                switch (opcode)
                {
                    case ILOpcode.sizeof_:
                    case ILOpcode.newarr:
                    case ILOpcode.stsfld:
                    case ILOpcode.ldsfld:
                    case ILOpcode.ldsflda:
                    case ILOpcode.stfld:
                    case ILOpcode.ldfld:
                    case ILOpcode.ldflda:
                    case ILOpcode.call:
                    case ILOpcode.calli:
                    case ILOpcode.callvirt:
                    case ILOpcode.newobj:
                    case ILOpcode.ldtoken:
                    case ILOpcode.ldftn:
                    case ILOpcode.ldvirtftn:
                    case ILOpcode.initobj:
                    case ILOpcode.stelem:
                    case ILOpcode.ldelem:
                    case ILOpcode.ldelema:
                    case ILOpcode.box:
                    case ILOpcode.unbox:
                    case ILOpcode.unbox_any:
                    case ILOpcode.jmp:
                    case ILOpcode.cpobj:
                    case ILOpcode.ldobj:
                    case ILOpcode.castclass:
                    case ILOpcode.isinst:
                    case ILOpcode.stobj:
                    case ILOpcode.refanyval:
                    case ILOpcode.mkrefany:
                    case ILOpcode.constrained:
                        if (opcode > ILOpcode.prefix1)
                        {
                            outputBodyBuilder.WriteByte((byte)ILOpcode.prefix1);
                            outputBodyBuilder.WriteByte((byte)(((int)opcode) & 0xff));
                        }
                        else
                        {
                            Debug.Assert(opcode != ILOpcode.prefix1);
                            outputBodyBuilder.WriteByte((byte)opcode);
                        }
                        outputBodyBuilder.WriteInt32(MetadataTokens.GetToken(writeContext.TokenMap.MapToken(MetadataTokens.EntityHandle(ilReader.ReadILToken()))));
                        break;

                    case ILOpcode.ldstr:
                        outputBodyBuilder.WriteByte((byte)opcode);
                        outputBodyBuilder.WriteInt32(
                            MetadataTokens.GetToken(
                                writeContext.MetadataBuilder.GetOrAddUserString(
                                    (string)methodIL.GetObject(ilReader.ReadILToken()))));
                        break;

                    case ILOpcode.switch_:
                        // switch is the opcode, then the number of targets N as int32, then N jump offsets as int32
                        // The offsets should not be affected by trimming, so we can write out exactly the same bytes
                        outputBodyBuilder.WriteByte((byte)opcode);
                        uint numTargets = ilReader.ReadILUInt32();
                        outputBodyBuilder.WriteUInt32(numTargets);
                        var byteCount = (int)(numTargets * sizeof(uint));
                        outputBodyBuilder.WriteBytes(bodyBytes, ilReader.Offset, byteCount);
                        ilReader.Seek(ilReader.Offset + byteCount);
                        break;

                    default:
                        outputBodyBuilder.WriteBytes(bodyBytes, offset, ILOpcodeHelper.GetSize(opcode));
                        ilReader.Skip(opcode);
                        break;
                }
            }

            MethodBodyStreamEncoder.MethodBody bodyEncoder = writeContext.MethodBodyEncoder.AddMethodBody(
                outputBodyBuilder.Count,
                methodIL.MaxStack,
                exceptionRegionCount: exceptionRegions.Length,
                hasSmallExceptionRegions: useSmallExceptionRegions,
                methodIL.GetLocals().Length == 0
                    ? default
                    : (StandaloneSignatureHandle)writeContext.TokenMap.MapToken(bodyBlock.LocalSignature),
                methodIL.IsInitLocals ? MethodBodyAttributes.InitLocals : MethodBodyAttributes.None);
            BlobWriter instructionsWriter = new(bodyEncoder.Instructions);

            ExceptionRegionEncoder exceptionRegionEncoder = bodyEncoder.ExceptionRegions;
            foreach (ILExceptionRegion exceptionRegion in exceptionRegions)
            {
                switch (exceptionRegion.Kind)
                {
                    case ILExceptionRegionKind.Catch:
                        exceptionRegionEncoder.AddCatch(
                            exceptionRegion.TryOffset,
                            exceptionRegion.TryLength,
                            exceptionRegion.HandlerOffset,
                            exceptionRegion.HandlerLength,
                            writeContext.TokenMap.MapToken(MetadataTokens.EntityHandle(exceptionRegion.ClassToken)));
                        break;

                    case ILExceptionRegionKind.Filter:
                        exceptionRegionEncoder.AddFilter(
                            exceptionRegion.TryOffset,
                            exceptionRegion.TryLength,
                            exceptionRegion.HandlerOffset,
                            exceptionRegion.HandlerLength,
                            exceptionRegion.FilterOffset);
                        break;

                    case ILExceptionRegionKind.Finally:
                        exceptionRegionEncoder.AddFinally(
                            exceptionRegion.TryOffset,
                            exceptionRegion.TryLength,
                            exceptionRegion.HandlerOffset,
                            exceptionRegion.HandlerLength);
                        break;

                    case ILExceptionRegionKind.Fault:
                        exceptionRegionEncoder.AddFault(
                            exceptionRegion.TryOffset,
                            exceptionRegion.TryLength,
                            exceptionRegion.HandlerOffset,
                            exceptionRegion.HandlerLength);
                        break;
                }
            }

            outputBodyBuilder.WriteContentTo(ref instructionsWriter);

            return bodyEncoder.Offset;
        }

        private MethodIL GetMethodIL(NodeFactory factory, EcmaMethod method)
        {
            Mono.Linker.AssemblyAction action = factory.Settings.CalculateAssemblyAction(_module);
            return action is Mono.Linker.AssemblyAction.Copy or Mono.Linker.AssemblyAction.CopyUsed
                ? EcmaMethodIL.Create(method)
                : factory.FlowAnnotations.ILProvider.GetMethodIL(method);
        }

        protected override string GetName(NodeFactory factory)
        {
            // TODO: would be nice to have a common formatter we can call into that also includes owning type
            MetadataReader reader = _module.MetadataReader;
            return "Method body for " + reader.GetString(reader.GetMethodDefinition(_methodHandle).Name);
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
