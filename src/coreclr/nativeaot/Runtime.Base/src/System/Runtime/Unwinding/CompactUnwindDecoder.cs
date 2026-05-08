// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Compact unwind is an Apple-specific optimization that encodes common unwind
// patterns as a single 32-bit integer instead of full DWARF CFI instructions.
// This is only used on macOS/iOS/tvOS/etc.
//
// This file provides a managed decoder for compact unwind encodings, equivalent
// to libunwind's CompactUnwinder_x86_64 and CompactUnwinder_arm64.
//
// TODO: Implement compact unwind decoding for Apple platforms.
// For the initial Linux x64 implementation, this is a stub.

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Compact unwind format constants.
    /// See mach-o/compact_unwind_encoding.h
    /// </summary>
    internal static class CompactUnwindConstants
    {
        // x86_64
        internal const uint UNWIND_X86_64_MODE_MASK = 0x0F000000;
        internal const uint UNWIND_X86_64_MODE_RBP_FRAME = 0x01000000;
        internal const uint UNWIND_X86_64_MODE_STACK_IMMD = 0x02000000;
        internal const uint UNWIND_X86_64_MODE_STACK_IND = 0x03000000;
        internal const uint UNWIND_X86_64_MODE_DWARF = 0x04000000;
        internal const uint UNWIND_X86_64_DWARF_SECTION_OFFSET = 0x00FFFFFF;

        internal const uint UNWIND_X86_64_RBP_FRAME_REGISTERS = 0x00007FFF;
        internal const uint UNWIND_X86_64_RBP_FRAME_OFFSET = 0x00FF0000;

        internal const uint UNWIND_X86_64_FRAMELESS_STACK_SIZE = 0x00FF0000;
        internal const uint UNWIND_X86_64_FRAMELESS_STACK_ADJUST = 0x0000E000;
        internal const uint UNWIND_X86_64_FRAMELESS_STACK_REG_COUNT = 0x00001C00;
        internal const uint UNWIND_X86_64_FRAMELESS_STACK_REG_PERMUTATION = 0x000003FF;

        // ARM64
        internal const uint UNWIND_ARM64_MODE_MASK = 0x0F000000;
        internal const uint UNWIND_ARM64_MODE_FRAMELESS = 0x02000000;
        internal const uint UNWIND_ARM64_MODE_DWARF = 0x03000000;
        internal const uint UNWIND_ARM64_MODE_FRAME = 0x04000000;
        internal const uint UNWIND_ARM64_DWARF_SECTION_OFFSET = 0x00FFFFFF;

        internal const uint UNWIND_ARM64_FRAME_X19_X20_PAIR = 0x00000001;
        internal const uint UNWIND_ARM64_FRAME_X21_X22_PAIR = 0x00000002;
        internal const uint UNWIND_ARM64_FRAME_X23_X24_PAIR = 0x00000004;
        internal const uint UNWIND_ARM64_FRAME_X25_X26_PAIR = 0x00000008;
        internal const uint UNWIND_ARM64_FRAME_X27_X28_PAIR = 0x00000010;
        internal const uint UNWIND_ARM64_FRAME_D8_D9_PAIR = 0x00000100;
        internal const uint UNWIND_ARM64_FRAME_D10_D11_PAIR = 0x00000200;
        internal const uint UNWIND_ARM64_FRAME_D12_D13_PAIR = 0x00000400;
        internal const uint UNWIND_ARM64_FRAME_D14_D15_PAIR = 0x00000800;
        internal const uint UNWIND_ARM64_FRAMELESS_STACK_SIZE_MASK = 0x00FFF000;

        internal static bool IsDwarfEncoding(uint format)
        {
            // Check if the compact encoding indicates a DWARF fallback.
            // Works for both x86_64 and ARM64 since MODE_DWARF values differ
            // but the mask is the same.
            uint mode = format & UNWIND_X86_64_MODE_MASK;
            return mode == UNWIND_X86_64_MODE_DWARF || mode == UNWIND_ARM64_MODE_DWARF;
        }
    }

    /// <summary>
    /// Decodes Apple compact unwind encodings and applies them to unwind a frame.
    /// </summary>
    internal static unsafe class CompactUnwindDecoder
    {
        // TODO: Implement stepWithCompactEncoding for x86_64 and ARM64.
        // This is a stub for the initial Linux x64 implementation.
        // On Linux, compact unwind sections are not present — all unwinding
        // uses DWARF .eh_frame.

        /// <summary>
        /// Attempt to unwind using a compact unwind encoding.
        /// Returns false if the encoding is not supported or indicates DWARF fallback.
        /// </summary>
        internal static bool TryStep(DwarfStepper.RegDisplay* _regs, uint _format, nuint _startIp)
        {
            _ = _regs;
            _ = _format;
            _ = _startIp;
            // On non-Apple platforms, compact unwind is not available
            return false;
        }
    }
}
