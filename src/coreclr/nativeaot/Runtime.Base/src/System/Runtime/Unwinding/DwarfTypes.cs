// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Describes how to recover a single register during unwinding.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DwarfRegisterLocation
    {
        internal DwarfRegisterLocationKind Kind;
        internal long Value; // offset, register number, or expression pointer depending on Kind
    }

    /// <summary>
    /// The result of interpreting DWARF CFI instructions for a given PC.
    /// Describes how to compute the CFA and restore each callee-saved register.
    /// </summary>
    internal unsafe struct DwarfPrologInfo
    {
        internal uint CfaRegister;
        internal long CfaRegisterOffset;
        internal nuint CfaExpression; // pointer to DWARF expression block (0 if CFA is register-based)

        internal long SpExtraArgSize;

        // Per-register save locations. Indexed by DWARF register number.
        internal SavedRegistersBuffer SavedRegisters;

        // AArch64 pointer authentication diversifier
        internal nuint PtrAuthDiversifier;

        [InlineArray(DwarfConstants.MaxRegisterNumber + 1)]
        internal struct SavedRegistersBuffer
        {
            private DwarfRegisterLocation _element0;
        }

        internal void Clear()
        {
            CfaRegister = 0;
            CfaRegisterOffset = 0;
            CfaExpression = 0;
            SpExtraArgSize = 0;
            PtrAuthDiversifier = 0;
            Unsafe.InitBlockUnaligned(
                ref Unsafe.As<DwarfRegisterLocation, byte>(ref SavedRegisters[0]),
                0,
                (uint)((DwarfConstants.MaxRegisterNumber + 1) * sizeof(DwarfRegisterLocation)));
        }
    }

    /// <summary>
    /// Parsed CIE (Common Information Entry) fields.
    /// </summary>
    internal struct DwarfCieInfo
    {
        internal nuint CieStart;
        internal nuint CieLength;
        internal nuint CieInstructions;
        internal nuint CieInstructionsEnd;

        internal byte PointerEncoding;
        internal byte LsdaEncoding;
        internal byte PersonalityEncoding;
        internal nuint PersonalityOffsetInCie;
        internal nuint Personality;

        internal uint CodeAlignFactor;
        internal int DataAlignFactor;
        internal uint ReturnAddressRegister;

        internal bool IsSignalFrame;
        internal bool FdesHaveAugmentationData;
        internal bool AddressesSignedWithBKey;
        internal bool MteTaggedFrame;
    }

    /// <summary>
    /// Parsed FDE (Frame Description Entry) fields.
    /// </summary>
    internal struct DwarfFdeInfo
    {
        internal nuint FdeStart;
        internal nuint FdeLength;
        internal nuint FdeInstructions;
        internal nuint FdeInstructionsEnd;

        internal nuint PcStart;
        internal nuint PcEnd;
        internal nuint Lsda;
    }

    /// <summary>
    /// Locations of unwind-related ELF/Mach-O sections for a loaded module.
    /// Populated by native code (findUnwindSections) and passed to the managed parser.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UnwindInfoSections
    {
        internal nuint DsoBase;
        internal nuint TextSegmentLength;

        internal nuint DwarfSection;        // .eh_frame base
        internal nuint DwarfSectionLength;
        internal nuint DwarfIndexSection;   // .eh_frame_hdr base
        internal nuint DwarfIndexSectionLength;

        internal nuint CompactUnwindSection;
        internal nuint CompactUnwindSectionLength;

        internal nuint ArmSection;          // ARM EHABI .ARM.exidx
        internal nuint ArmSectionLength;
    }

    /// <summary>
    /// Information about the unwind procedure for a given PC.
    /// Equivalent to libunwind's unw_proc_info_t.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct UnwindProcInfo
    {
        internal nuint StartIp;
        internal nuint EndIp;
        internal nuint Lsda;
        internal nuint Handler;
        internal nuint Gp;
        internal uint Flags;
        internal uint Format;        // compact unwind encoding, or 0 for DWARF
        internal nuint UnwindInfoSize;
        internal nuint UnwindInfo;   // pointer to FDE or compact unwind data
        internal nuint Extra;        // mach header or image base
    }
}
