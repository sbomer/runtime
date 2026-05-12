// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Uses DWARF CFI register restore rules (DwarfPrologInfo) to virtually
    /// unwind one stack frame, updating the register display in place.
    ///
    /// This is the managed equivalent of libunwind's DwarfInstructions::stepWithDwarf
    /// and the CompactUnwinder_x86_64 / CompactUnwinder_arm64 classes.
    ///
    /// Initial implementation: x86_64 only. Other architectures will be added
    /// incrementally.
    /// </summary>
    internal static unsafe class DwarfStepper
    {
        /// <summary>
        /// Unwind one frame using DWARF CFI rules.
        /// The FDE is decoded, CFI instructions are interpreted, and register
        /// values are restored according to the resulting PrologInfo.
        /// </summary>
        /// <param name="regs">Pointer to the native REGDISPLAY to update.</param>
        /// <param name="fdeStart">Start of the FDE record in .eh_frame.</param>
        /// <param name="ehFrame">Start of the .eh_frame section.</param>
        /// <param name="ehFrameLength">Length of the .eh_frame section.</param>
        /// <param name="pc">Current instruction pointer to unwind from.</param>
        /// <returns>true if the frame was successfully unwound.</returns>
        internal static bool TryStepWithDwarf(
            RegDisplay* regs,
            nuint fdeStart,
            nuint ehFrame,
            nuint ehFrameLength,
            nuint pc)
        {
            // Parse the FDE and its CIE
            byte* ehFrameBase = (byte*)ehFrame;
            byte* ehFrameEnd = ehFrameBase + ehFrameLength;

            if (!DwarfFdeParser.TryParseFde((byte*)fdeStart, ehFrameBase, ehFrameEnd,
                out DwarfFdeInfo fde, out DwarfCieInfo cie))
                return false;

            // Interpret CFI instructions to build register restore rules
            if (!DwarfCfiInterpreter.TryInterpretCfi(ref cie, ref fde, pc, out DwarfPrologInfo prolog))
                return false;

            // Compute the CFA (Canonical Frame Address)
            nuint cfa;
            if (prolog.CfaExpression != 0)
            {
                if (!DwarfExpressionEvaluator.TryEvaluate(
                    (byte*)prolog.CfaExpression,
                    0, // no initial value for CFA expression
                    &GetRegisterForExpression,
                    regs,
                    out cfa))
                    return false;
            }
            else if (prolog.CfaRegister != 0)
            {
                nuint cfaRegValue = GetRegister(regs, (int)prolog.CfaRegister);
                cfa = (nuint)((long)cfaRegValue + prolog.CfaRegisterOffset);
            }
            else
            {
                return false;
            }

            // Restore registers according to the prolog info.
            // Process all registers, setting SP to CFA first as the default.
            nuint newSP = cfa;
            nuint returnAddress = 0;
            nuint returnAddressLocation;

            int lastReg = GetLastDwarfRegNum();

            for (int i = 0; i <= lastReg; i++)
            {
                ref DwarfRegisterLocation loc = ref prolog.SavedRegisters[i];
                if (loc.Kind == DwarfRegisterLocationKind.Unused)
                {
                    // For the return address register, a leaf function keeps RA in-register
                    if (i == (int)cie.ReturnAddressRegister)
                        returnAddress = GetRegister(regs, (int)cie.ReturnAddressRegister);
                    continue;
                }

                if (i == (int)cie.ReturnAddressRegister)
                {
                    if (!TryGetSavedRegister(regs, cfa, ref loc, out returnAddress, out returnAddressLocation))
                        return false;
                    // Also update the register itself if it's a valid general register
                    if (IsValidRegister(i))
                        SetRegister(regs, i, returnAddress, returnAddressLocation);
                }
                else if (IsValidRegister(i))
                {
                    if (!TryGetSavedRegister(regs, cfa, ref loc, out nuint value, out nuint location))
                        return false;
                    SetRegister(regs, i, value, location);
                }
            }

            // Set SP to CFA (may have been overridden by a CFI directive above)
            SetSP(regs, newSP);

            // Set IP to the return address
            SetIP(regs, returnAddress);

            return true;
        }

        /// <summary>
        /// Recover a saved register value from a DwarfRegisterLocation.
        /// </summary>
        private static bool TryGetSavedRegister(
            RegDisplay* regs,
            nuint cfa,
            ref DwarfRegisterLocation loc,
            out nuint value,
            out nuint location)
        {
            value = 0;
            location = 0;

            switch (loc.Kind)
            {
                case DwarfRegisterLocationKind.InCFA:
                    location = (nuint)((long)cfa + loc.Value);
                    value = *(nuint*)location;
                    return true;

                case DwarfRegisterLocationKind.OffsetFromCFA:
                    value = (nuint)((long)cfa + loc.Value);
                    return true;

                case DwarfRegisterLocationKind.InRegister:
                    value = GetRegister(regs, (int)loc.Value);
                    return true;

                case DwarfRegisterLocationKind.AtExpression:
                    if (!DwarfExpressionEvaluator.TryEvaluate(
                        (byte*)loc.Value, cfa,
                        &GetRegisterForExpression, regs,
                        out location))
                        return false;
                    value = *(nuint*)location;
                    return true;

                case DwarfRegisterLocationKind.IsExpression:
                    return DwarfExpressionEvaluator.TryEvaluate(
                        (byte*)loc.Value, cfa,
                        &GetRegisterForExpression, regs,
                        out value);

                case DwarfRegisterLocationKind.Undefined:
                    value = 0;
                    return true;

                default:
                    return false;
            }
        }

        // Callback for DwarfExpressionEvaluator — reads a register by DWARF number
        private static nuint GetRegisterForExpression(void* context, int regNum)
        {
            return GetRegister((RegDisplay*)context, regNum);
        }

        // ============================================================
        // x86_64 REGDISPLAY mapping
        //
        // DWARF register numbers for x86_64:
        //   0=RAX, 1=RDX, 2=RCX, 3=RBX, 4=RSI, 5=RDI, 6=RBP, 7=RSP,
        //   8-15=R8-R15, 16=RIP
        // ============================================================

        // The managed REGDISPLAY mirror. This must match the native layout exactly.
        // On x86_64 Unix: pointers to register save locations, then SP and IP.
        [StructLayout(LayoutKind.Sequential)]
        internal struct RegDisplay
        {
            internal nuint* pRax;
            internal nuint* pRcx;
            internal nuint* pRdx;
            internal nuint* pRbx;
            internal nuint* pRbp;
            internal nuint* pRsi;
            internal nuint* pRdi;
            internal nuint* pR8;
            internal nuint* pR9;
            internal nuint* pR10;
            internal nuint* pR11;
            internal nuint* pR12;
            internal nuint* pR13;
            internal nuint* pR14;
            internal nuint* pR15;
            internal nuint SP;
            internal nuint IP;
        }

        // DWARF register number constants for x86_64
        private const int RegRax = 0;
        private const int RegRdx = 1;
        private const int RegRcx = 2;
        private const int RegRbx = 3;
        private const int RegRsi = 4;
        private const int RegRdi = 5;
        private const int RegRbp = 6;
        private const int RegRsp = 7;
        private const int RegR8 = 8;
        private const int RegR9 = 9;
        private const int RegR10 = 10;
        private const int RegR11 = 11;
        private const int RegR12 = 12;
        private const int RegR13 = 13;
        private const int RegR14 = 14;
        private const int RegR15 = 15;
        private const int RegRip = 16;

        private static int GetLastDwarfRegNum() => RegRip;

        private static bool IsValidRegister(int regNum) => regNum >= RegRax && regNum <= RegRip;

        private static nuint GetRegister(RegDisplay* regs, int regNum)
        {
            switch (regNum)
            {
                case RegRax: return *regs->pRax;
                case RegRdx: return *regs->pRdx;
                case RegRcx: return *regs->pRcx;
                case RegRbx: return *regs->pRbx;
                case RegRsi: return *regs->pRsi;
                case RegRdi: return *regs->pRdi;
                case RegRbp: return *regs->pRbp;
                case RegRsp: return regs->SP;
                case RegR8:  return *regs->pR8;
                case RegR9:  return *regs->pR9;
                case RegR10: return *regs->pR10;
                case RegR11: return *regs->pR11;
                case RegR12: return *regs->pR12;
                case RegR13: return *regs->pR13;
                case RegR14: return *regs->pR14;
                case RegR15: return *regs->pR15;
                case RegRip: return regs->IP;
                default: return 0;
            }
        }

        private static void SetRegister(RegDisplay* regs, int regNum, nuint value, nuint location)
        {
            // For most registers, we update the pointer to point to the save location,
            // not the value itself. The value is read through the pointer by the runtime.
            switch (regNum)
            {
                case RegRax: regs->pRax = (nuint*)location; break;
                case RegRdx: regs->pRdx = (nuint*)location; break;
                case RegRcx: regs->pRcx = (nuint*)location; break;
                case RegRbx: regs->pRbx = (nuint*)location; break;
                case RegRsi: regs->pRsi = (nuint*)location; break;
                case RegRdi: regs->pRdi = (nuint*)location; break;
                case RegRbp: regs->pRbp = (nuint*)location; break;
                case RegRsp: regs->SP = value; break;
                case RegR8:  regs->pR8 = (nuint*)location; break;
                case RegR9:  regs->pR9 = (nuint*)location; break;
                case RegR10: regs->pR10 = (nuint*)location; break;
                case RegR11: regs->pR11 = (nuint*)location; break;
                case RegR12: regs->pR12 = (nuint*)location; break;
                case RegR13: regs->pR13 = (nuint*)location; break;
                case RegR14: regs->pR14 = (nuint*)location; break;
                case RegR15: regs->pR15 = (nuint*)location; break;
                case RegRip: regs->IP = value; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetSP(RegDisplay* regs, nuint sp) => regs->SP = sp;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SetIP(RegDisplay* regs, nuint ip) => regs->IP = ip;
    }
}
