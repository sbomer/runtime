// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Parses .eh_frame_hdr sections to locate FDE records for a given PC,
    /// and parses CIE/FDE records from .eh_frame sections.
    /// </summary>
    internal static unsafe class DwarfCfiParser
    {
        /// <summary>
        /// Parse a CIE record starting at the given address.
        /// </summary>
        internal static bool TryParseCie(byte* cieStart, byte* sectionEnd, out DwarfCieInfo cie)
        {
            cie = default;
            byte* p = cieStart;

            // Read length (4 bytes; 0xFFFFFFFF means 64-bit DWARF which we don't support)
            if (!DwarfReader.TryReadUInt32(ref p, sectionEnd, out uint length))
                return false;
            if (length == 0 || length == 0xFFFFFFFF)
                return false;

            byte* cieEnd = p + length;
            if (cieEnd > sectionEnd)
                return false;

            cie.CieStart = (nuint)cieStart;
            cie.CieLength = (nuint)(cieEnd - cieStart);

            // CIE ID: must be 0 for .eh_frame CIE
            if (!DwarfReader.TryReadUInt32(ref p, cieEnd, out uint cieId))
                return false;
            if (cieId != 0)
                return false; // This is an FDE, not a CIE

            // Version
            if (!DwarfReader.TryReadByte(ref p, cieEnd, out byte version))
                return false;
            if (version != 1 && version != 3)
                return false;

            // Augmentation string (null-terminated)
            byte* augStart = p;
            while (p < cieEnd && *p != 0)
                p++;
            if (p >= cieEnd)
                return false;

            int augLen = (int)(p - augStart);
            p++; // skip null terminator

            // Code alignment factor (ULEB128)
            if (!DwarfReader.TryReadULEB128(ref p, cieEnd, out nuint codeAlign))
                return false;
            cie.CodeAlignFactor = (uint)codeAlign;

            // Data alignment factor (SLEB128)
            if (!DwarfReader.TryReadSLEB128(ref p, cieEnd, out long dataAlign))
                return false;
            cie.DataAlignFactor = (int)dataAlign;

            // Return address register
            if (version == 1)
            {
                if (!DwarfReader.TryReadByte(ref p, cieEnd, out byte ra))
                    return false;
                cie.ReturnAddressRegister = ra;
            }
            else
            {
                if (!DwarfReader.TryReadULEB128(ref p, cieEnd, out nuint ra))
                    return false;
                cie.ReturnAddressRegister = (uint)ra;
            }

            // Default encodings
            cie.PointerEncoding = DwarfConstants.DW_EH_PE_absptr;
            cie.LsdaEncoding = DwarfConstants.DW_EH_PE_omit;
            cie.PersonalityEncoding = DwarfConstants.DW_EH_PE_omit;

            // Parse augmentation data
            if (augLen > 0 && augStart[0] == (byte)'z')
            {
                cie.FdesHaveAugmentationData = true;

                // Augmentation data length
                if (!DwarfReader.TryReadULEB128(ref p, cieEnd, out nuint augDataLen))
                    return false;

                byte* augDataEnd = p + augDataLen;
                if (augDataEnd > cieEnd)
                    return false;

                // Parse augmentation characters (skip 'z' which we already handled)
                for (int i = 1; i < augLen; i++)
                {
                    byte augChar = augStart[i];
                    switch (augChar)
                    {
                        case (byte)'P':
                            // Personality encoding + pointer
                            if (!DwarfReader.TryReadByte(ref p, augDataEnd, out byte persEnc))
                                return false;
                            cie.PersonalityEncoding = persEnc;
                            cie.PersonalityOffsetInCie = (nuint)(p - cieStart);
                            if (!DwarfReader.TryReadEncodedPointer(ref p, augDataEnd, persEnc, 0, 0, 0, out nuint personality))
                                return false;
                            cie.Personality = personality;
                            break;

                        case (byte)'L':
                            if (!DwarfReader.TryReadByte(ref p, augDataEnd, out byte lsdaEnc))
                                return false;
                            cie.LsdaEncoding = lsdaEnc;
                            break;

                        case (byte)'R':
                            if (!DwarfReader.TryReadByte(ref p, augDataEnd, out byte ptrEnc))
                                return false;
                            cie.PointerEncoding = ptrEnc;
                            break;

                        case (byte)'S':
                            cie.IsSignalFrame = true;
                            break;

                        case (byte)'B':
                            cie.AddressesSignedWithBKey = true;
                            break;

                        case (byte)'G':
                            cie.MteTaggedFrame = true;
                            break;

                        default:
                            // Unknown augmentation character — skip remaining
                            p = augDataEnd;
                            i = augLen; // break loop
                            break;
                    }
                }

                // Advance past any remaining augmentation data
                p = augDataEnd;
            }

            cie.CieInstructions = (nuint)p;
            cie.CieInstructionsEnd = (nuint)cieEnd;

            return true;
        }

        /// <summary>
        /// Parse an FDE record and its associated CIE.
        /// </summary>
        internal static bool TryParseFde(
            byte* fdeStart, byte* sectionStart, byte* sectionEnd,
            out DwarfFdeInfo fde, out DwarfCieInfo cie)
        {
            fde = default;
            cie = default;
            byte* p = fdeStart;

            // Read length
            if (!DwarfReader.TryReadUInt32(ref p, sectionEnd, out uint length))
                return false;
            if (length == 0 || length == 0xFFFFFFFF)
                return false;

            byte* fdeEnd = p + length;
            if (fdeEnd > sectionEnd)
                return false;

            fde.FdeStart = (nuint)fdeStart;
            fde.FdeLength = (nuint)(fdeEnd - fdeStart);

            // CIE pointer: offset from current position back to CIE
            byte* ciePointerPos = p;
            if (!DwarfReader.TryReadUInt32(ref p, fdeEnd, out uint ciePointer))
                return false;
            if (ciePointer == 0)
                return false; // This is a CIE, not an FDE

            byte* cieAddr = ciePointerPos - ciePointer;
            if (cieAddr < sectionStart)
                return false;

            // Parse the referenced CIE
            if (!TryParseCie(cieAddr, sectionEnd, out cie))
                return false;

            // Decode PC start and range using the CIE's pointer encoding
            if (!DwarfReader.TryReadEncodedPointer(ref p, fdeEnd, cie.PointerEncoding, 0, 0, 0, out nuint pcStart))
                return false;

            // PC range: same format but absolute (no application)
            byte rangeEncoding = (byte)(cie.PointerEncoding & DwarfConstants.DW_EH_PE_FORMAT_MASK);
            if (!DwarfReader.TryReadEncodedPointer(ref p, fdeEnd, rangeEncoding, 0, 0, 0, out nuint pcRange))
                return false;

            fde.PcStart = pcStart;
            fde.PcEnd = pcStart + pcRange;

            // Parse augmentation data if present
            if (cie.FdesHaveAugmentationData)
            {
                if (!DwarfReader.TryReadULEB128(ref p, fdeEnd, out nuint augDataLen))
                    return false;

                byte* augDataStart = p;
                byte* augDataEnd = p + augDataLen;
                if (augDataEnd > fdeEnd)
                    return false;

                // LSDA pointer (if CIE has L augmentation)
                if (cie.LsdaEncoding != DwarfConstants.DW_EH_PE_omit)
                {
                    byte* lsdaP = augDataStart;
                    if (DwarfReader.TryReadEncodedPointer(ref lsdaP, augDataEnd, cie.LsdaEncoding, 0, 0, 0, out nuint lsda))
                        fde.Lsda = lsda;
                }

                p = augDataEnd;
            }

            fde.FdeInstructions = (nuint)p;
            fde.FdeInstructionsEnd = (nuint)fdeEnd;

            return true;
        }

        /// <summary>
        /// Search the .eh_frame_hdr binary search table to find the FDE for a given PC.
        /// </summary>
        internal static bool TryFindFdeFromEhFrameHdr(
            byte* ehFrameHdr, nuint ehFrameHdrLength,
            nuint pc,
            byte* ehFrame, nuint ehFrameLength,
            out DwarfFdeInfo fde, out DwarfCieInfo cie)
        {
            fde = default;
            cie = default;

            byte* p = ehFrameHdr;
            byte* end = ehFrameHdr + ehFrameHdrLength;

            // .eh_frame_hdr format:
            // byte version (must be 1)
            // byte eh_frame_ptr_enc
            // byte fde_count_enc
            // byte table_enc
            // encoded eh_frame_ptr
            // encoded fde_count
            // binary search table: (encoded initial_location, encoded fde_addr) pairs

            if (!DwarfReader.TryReadByte(ref p, end, out byte version) || version != 1)
                return false;

            if (!DwarfReader.TryReadByte(ref p, end, out byte ehFramePtrEnc))
                return false;
            if (!DwarfReader.TryReadByte(ref p, end, out byte fdeCountEnc))
                return false;
            if (!DwarfReader.TryReadByte(ref p, end, out byte tableEnc))
                return false;

            // Read eh_frame pointer (we already have it, but need to advance past it)
            if (!DwarfReader.TryReadEncodedPointer(ref p, end, ehFramePtrEnc, (nuint)ehFrameHdr, 0, 0, out _))
                return false;

            // Read FDE count
            if (!DwarfReader.TryReadEncodedPointer(ref p, end, fdeCountEnc, (nuint)ehFrameHdr, 0, 0, out nuint fdeCount))
                return false;

            if (fdeCount == 0 || tableEnc == DwarfConstants.DW_EH_PE_omit)
                return false;

            // Compute entry size for the binary search table
            int entrySize = GetEncodedPointerSize(tableEnc);
            if (entrySize <= 0)
                return false;

            int tableEntrySize = entrySize * 2; // (initial_location, fde_addr) pair
            byte* tableStart = p;

            // Binary search the table
            int low = 0;
            int high = (int)fdeCount - 1;
            nuint bestFdeAddr = 0;

            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                byte* entryP = tableStart + (mid * tableEntrySize);
                byte* entryEnd = entryP + tableEntrySize;

                if (entryEnd > end)
                    return false;

                byte* readP = entryP;
                if (!DwarfReader.TryReadEncodedPointer(ref readP, entryEnd, tableEnc, (nuint)ehFrameHdr, 0, 0, out nuint entryPc))
                    return false;

                if (pc < entryPc)
                {
                    high = mid - 1;
                }
                else
                {
                    // Read the FDE address
                    if (!DwarfReader.TryReadEncodedPointer(ref readP, entryEnd, tableEnc, (nuint)ehFrameHdr, 0, 0, out nuint fdeAddr))
                        return false;
                    bestFdeAddr = fdeAddr;
                    low = mid + 1;
                }
            }

            if (bestFdeAddr == 0)
                return false;

            // Parse the FDE at the found address
            byte* fdeLoc = (byte*)bestFdeAddr;
            byte* ehFrameEnd = ehFrame + ehFrameLength;
            if (fdeLoc < ehFrame || fdeLoc >= ehFrameEnd)
                return false;

            if (!TryParseFde(fdeLoc, ehFrame, ehFrameEnd, out fde, out cie))
                return false;

            // Verify that the PC falls within this FDE's range
            if (pc < fde.PcStart || pc >= fde.PcEnd)
                return false;

            return true;
        }

        /// <summary>
        /// Linear scan of .eh_frame to find the FDE containing a given PC.
        /// Used as fallback when .eh_frame_hdr is not available.
        /// </summary>
        internal static bool TryFindFdeFromEhFrame(
            byte* ehFrame, nuint ehFrameLength,
            nuint pc,
            out DwarfFdeInfo fde, out DwarfCieInfo cie)
        {
            fde = default;
            cie = default;

            byte* p = ehFrame;
            byte* end = ehFrame + ehFrameLength;

            while (p < end)
            {
                byte* recordStart = p;

                // Read length
                if (!DwarfReader.TryReadUInt32(ref p, end, out uint length))
                    return false;

                // Zero-length terminator
                if (length == 0)
                    return false;

                // 64-bit DWARF not supported
                if (length == 0xFFFFFFFF)
                    return false;

                byte* recordEnd = p + length;
                if (recordEnd > end)
                    return false;

                // Read CIE/FDE discriminator
                if (!DwarfReader.TryReadUInt32(ref p, recordEnd, out uint id))
                {
                    p = recordEnd;
                    continue;
                }

                // id == 0 means CIE, skip it
                if (id == 0)
                {
                    p = recordEnd;
                    continue;
                }

                // This is an FDE — try to parse it
                if (TryParseFde(recordStart, ehFrame, end, out DwarfFdeInfo candidateFde, out DwarfCieInfo candidateCie))
                {
                    if (pc >= candidateFde.PcStart && pc < candidateFde.PcEnd)
                    {
                        fde = candidateFde;
                        cie = candidateCie;
                        return true;
                    }
                }

                p = recordEnd;
            }

            return false;
        }

        /// <summary>
        /// Interpret DWARF CFI instructions (from both CIE and FDE) to build
        /// the register restore rules for a given PC.
        /// </summary>
        internal static bool TryInterpretCfi(
            ref DwarfCieInfo cie,
            ref DwarfFdeInfo fde,
            nuint pc,
            out DwarfPrologInfo prologInfo)
        {
            prologInfo = default;
            prologInfo.Clear();

            // First, execute CIE initial instructions to establish the initial rule set
            if (!InterpretInstructions(
                (byte*)cie.CieInstructions, (byte*)cie.CieInstructionsEnd,
                ref cie, fde.PcStart, nuint.MaxValue, ref prologInfo))
                return false;

            // Save initial state (for DW_CFA_restore)
            DwarfPrologInfo initialState = prologInfo;

            // Then execute FDE instructions up to the target PC
            if (!InterpretInstructions(
                (byte*)fde.FdeInstructions, (byte*)fde.FdeInstructionsEnd,
                ref cie, fde.PcStart, pc, ref prologInfo))
            {
                // On failure, restore initial state
                prologInfo = initialState;
                return false;
            }

            return true;
        }

        private static bool InterpretInstructions(
            byte* instructions, byte* instructionsEnd,
            ref DwarfCieInfo cie,
            nuint pcStart, nuint targetPc,
            ref DwarfPrologInfo prolog)
        {
            byte* p = instructions;
            byte* end = instructionsEnd;
            nuint codeOffset = pcStart;

            // Remember/restore state stack (fixed depth)
            RememberStateStack stateStack = default;
            int stateDepth = 0;

            while (p < end)
            {
                // Stop if we've advanced past the target PC
                if (codeOffset > targetPc)
                    break;

                byte opcode = *p;
                p++;

                // Check high 2 bits for primary opcodes
                byte primaryOp = (byte)(opcode & 0xC0);
                byte operand = (byte)(opcode & 0x3F);

                if (primaryOp == DwarfConstants.DW_CFA_advance_loc)
                {
                    codeOffset += (nuint)(operand * cie.CodeAlignFactor);
                    continue;
                }
                else if (primaryOp == DwarfConstants.DW_CFA_offset)
                {
                    if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                        return false;

                    int reg = operand;
                    if (reg > DwarfConstants.MaxRegisterNumber)
                        return false;

                    prolog.SavedRegisters[reg].Kind = DwarfRegisterLocationKind.InCFA;
                    prolog.SavedRegisters[reg].Value = (long)offset * cie.DataAlignFactor;
                    continue;
                }
                else if (primaryOp == DwarfConstants.DW_CFA_restore)
                {
                    // Restore to initial state — handled by caller via initialState copy
                    int reg = operand;
                    if (reg > DwarfConstants.MaxRegisterNumber)
                        return false;
                    prolog.SavedRegisters[reg].Kind = DwarfRegisterLocationKind.Unused;
                    prolog.SavedRegisters[reg].Value = 0;
                    continue;
                }

                // Extended opcodes (primary bits = 0)
                switch (opcode)
                {
                    case DwarfConstants.DW_CFA_nop:
                        break;

                    case DwarfConstants.DW_CFA_set_loc:
                        if (!DwarfReader.TryReadPointer(ref p, end, out nuint newLoc))
                            return false;
                        codeOffset = newLoc;
                        break;

                    case DwarfConstants.DW_CFA_advance_loc1:
                        if (!DwarfReader.TryReadByte(ref p, end, out byte delta1))
                            return false;
                        codeOffset += (nuint)(delta1 * cie.CodeAlignFactor);
                        break;

                    case DwarfConstants.DW_CFA_advance_loc2:
                        if (!DwarfReader.TryReadUInt16(ref p, end, out ushort delta2))
                            return false;
                        codeOffset += (nuint)(delta2 * cie.CodeAlignFactor);
                        break;

                    case DwarfConstants.DW_CFA_advance_loc4:
                        if (!DwarfReader.TryReadUInt32(ref p, end, out uint delta4))
                            return false;
                        codeOffset += (nuint)(delta4 * cie.CodeAlignFactor);
                        break;

                    case DwarfConstants.DW_CFA_offset_extended:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.InCFA;
                        prolog.SavedRegisters[(int)reg].Value = (long)offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_restore_extended:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.Unused;
                        prolog.SavedRegisters[(int)reg].Value = 0;
                        break;
                    }

                    case DwarfConstants.DW_CFA_undefined:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.Undefined;
                        prolog.SavedRegisters[(int)reg].Value = 0;
                        break;
                    }

                    case DwarfConstants.DW_CFA_same_value:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        // "same value" means the register is unchanged — treat as unused
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.Unused;
                        break;
                    }

                    case DwarfConstants.DW_CFA_register:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint otherReg))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.InRegister;
                        prolog.SavedRegisters[(int)reg].Value = (long)otherReg;
                        break;
                    }

                    case DwarfConstants.DW_CFA_remember_state:
                    {
                        if (stateDepth >= DwarfConstants.MaxRememberStateDepth)
                            return false;
                        stateStack[stateDepth] = prolog;
                        stateDepth++;
                        break;
                    }

                    case DwarfConstants.DW_CFA_restore_state:
                    {
                        if (stateDepth <= 0)
                            return false;
                        stateDepth--;
                        prolog = stateStack[stateDepth];
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                            return false;
                        prolog.CfaRegister = (uint)reg;
                        prolog.CfaRegisterOffset = (long)offset;
                        prolog.CfaExpression = 0;
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa_register:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        prolog.CfaRegister = (uint)reg;
                        prolog.CfaExpression = 0;
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa_offset:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                            return false;
                        prolog.CfaRegisterOffset = (long)offset;
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa_expression:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint exprLen))
                            return false;
                        if (p + exprLen > end)
                            return false;
                        prolog.CfaExpression = (nuint)p;
                        prolog.CfaRegister = 0;
                        p += exprLen;
                        break;
                    }

                    case DwarfConstants.DW_CFA_expression:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint exprLen))
                            return false;
                        if (p + exprLen > end)
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.AtExpression;
                        prolog.SavedRegisters[(int)reg].Value = (long)(nint)p;
                        p += exprLen;
                        break;
                    }

                    case DwarfConstants.DW_CFA_offset_extended_sf:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.InCFA;
                        prolog.SavedRegisters[(int)reg].Value = offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa_sf:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                            return false;
                        prolog.CfaRegister = (uint)reg;
                        prolog.CfaRegisterOffset = offset * cie.DataAlignFactor;
                        prolog.CfaExpression = 0;
                        break;
                    }

                    case DwarfConstants.DW_CFA_def_cfa_offset_sf:
                    {
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                            return false;
                        prolog.CfaRegisterOffset = offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_val_offset:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.OffsetFromCFA;
                        prolog.SavedRegisters[(int)reg].Value = (long)offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_val_offset_sf:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.OffsetFromCFA;
                        prolog.SavedRegisters[(int)reg].Value = offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_val_expression:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint exprLen))
                            return false;
                        if (p + exprLen > end)
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.IsExpression;
                        prolog.SavedRegisters[(int)reg].Value = (long)(nint)p;
                        p += exprLen;
                        break;
                    }

                    case DwarfConstants.DW_CFA_GNU_args_size:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint argsSize))
                            return false;
                        prolog.SpExtraArgSize = (long)argsSize;
                        break;
                    }

                    case DwarfConstants.DW_CFA_GNU_negative_offset_extended:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint reg))
                            return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint offset))
                            return false;
                        if ((int)reg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        prolog.SavedRegisters[(int)reg].Kind = DwarfRegisterLocationKind.InCFA;
                        prolog.SavedRegisters[(int)reg].Value = -(long)offset * cie.DataAlignFactor;
                        break;
                    }

                    case DwarfConstants.DW_CFA_AARCH64_negate_ra_state:
                    {
                        // Toggle RA signing state (AArch64 pointer authentication)
                        int raReg = (int)cie.ReturnAddressRegister;
                        if (raReg > DwarfConstants.MaxRegisterNumber)
                            return false;
                        // Toggle: if InCFA with value 0 or 1, flip it
                        ref DwarfRegisterLocation raLoc = ref prolog.SavedRegisters[raReg];
                        if (raLoc.Kind == DwarfRegisterLocationKind.Unused)
                        {
                            raLoc.Kind = DwarfRegisterLocationKind.InCFA;
                            raLoc.Value = 1; // RA is signed
                        }
                        else
                        {
                            raLoc.Value ^= 1;
                        }
                        break;
                    }

                    default:
                        // Unknown opcode — cannot continue safely
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Get the size of an encoded pointer for a given encoding.
        /// Returns -1 for variable-length or unsupported encodings.
        /// </summary>
        private static int GetEncodedPointerSize(byte encoding)
        {
            switch (encoding & DwarfConstants.DW_EH_PE_FORMAT_MASK)
            {
                case DwarfConstants.DW_EH_PE_absptr:
                    return sizeof(nuint);
                case DwarfConstants.DW_EH_PE_udata2:
                case DwarfConstants.DW_EH_PE_sdata2:
                    return 2;
                case DwarfConstants.DW_EH_PE_udata4:
                case DwarfConstants.DW_EH_PE_sdata4:
                    return 4;
                case DwarfConstants.DW_EH_PE_udata8:
                case DwarfConstants.DW_EH_PE_sdata8:
                    return 8;
                default:
                    return -1;
            }
        }

        [InlineArray(DwarfConstants.MaxRememberStateDepth)]
        private struct RememberStateStack
        {
            private DwarfPrologInfo _element0;
        }
    }
}
