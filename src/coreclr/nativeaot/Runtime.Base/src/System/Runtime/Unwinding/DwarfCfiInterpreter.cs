// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Interprets DWARF CFI (Call Frame Information) instructions to build
    /// register restore rules for a given PC. This processes the CFI bytecode
    /// that the ObjectWriter's DwarfFde emits via CfiCodeToInstructions.
    /// </summary>
    internal static unsafe class DwarfCfiInterpreter
    {
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

        [InlineArray(DwarfConstants.MaxRememberStateDepth)]
        private struct RememberStateStack
        {
            private DwarfPrologInfo _element0;
        }
    }
}
