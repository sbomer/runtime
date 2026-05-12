// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// DWARF CFI opcodes, pointer encodings, and expression opcodes used by the
// managed unwinder. The DW_EH_PE_* and DW_CFA_* sections intentionally
// mirror the layout of ILCompiler.ObjectWriter.DwarfNative so that the
// two can be unified into a shared source file in the future.

namespace System.Runtime.Unwinding
{
    internal static class DwarfConstants
    {
        // ===================================================================
        // DW_EH_PE pointer encoding — mirrors DwarfNative DW_EH_PE block
        // ===================================================================

        internal const byte DW_EH_PE_absptr = 0x00;
        internal const byte DW_EH_PE_omit = 0xFF;
        internal const byte DW_EH_PE_ptr = 0x00;
        internal const byte DW_EH_PE_uleb128 = 0x01;
        internal const byte DW_EH_PE_udata2 = 0x02;
        internal const byte DW_EH_PE_udata4 = 0x03;
        internal const byte DW_EH_PE_udata8 = 0x04;
        internal const byte DW_EH_PE_sleb128 = 0x09;
        internal const byte DW_EH_PE_sdata2 = 0x0A;
        internal const byte DW_EH_PE_sdata4 = 0x0B;
        internal const byte DW_EH_PE_sdata8 = 0x0C;
        internal const byte DW_EH_PE_signed = 0x08;
        internal const byte DW_EH_PE_pcrel = 0x10;
        internal const byte DW_EH_PE_textrel = 0x20;
        internal const byte DW_EH_PE_datarel = 0x30;
        internal const byte DW_EH_PE_funcrel = 0x40;
        internal const byte DW_EH_PE_aligned = 0x50;
        internal const byte DW_EH_PE_indirect = 0x80;

        // Masks for encoding components
        internal const byte DW_EH_PE_FORMAT_MASK = 0x0F;
        internal const byte DW_EH_PE_APPL_MASK = 0x70;

        // ===================================================================
        // DW_CFA opcodes — mirrors DwarfNative DW_CFA block
        // ===================================================================

        internal const byte DW_CFA_nop = 0x0;
        internal const byte DW_CFA_set_loc = 0x1;
        internal const byte DW_CFA_advance_loc1 = 0x2;
        internal const byte DW_CFA_advance_loc2 = 0x3;
        internal const byte DW_CFA_advance_loc4 = 0x4;
        internal const byte DW_CFA_offset_extended = 0x5;
        internal const byte DW_CFA_restore_extended = 0x6;
        internal const byte DW_CFA_undefined = 0x7;
        internal const byte DW_CFA_same_value = 0x8;
        internal const byte DW_CFA_register = 0x9;
        internal const byte DW_CFA_remember_state = 0xA;
        internal const byte DW_CFA_restore_state = 0xB;
        internal const byte DW_CFA_def_cfa = 0xC;
        internal const byte DW_CFA_def_cfa_register = 0xD;
        internal const byte DW_CFA_def_cfa_offset = 0xE;
        internal const byte DW_CFA_def_cfa_expression = 0xF;
        internal const byte DW_CFA_expression = 0x10;
        internal const byte DW_CFA_offset_extended_sf = 0x11;
        internal const byte DW_CFA_def_cfa_sf = 0x12;
        internal const byte DW_CFA_def_cfa_offset_sf = 0x13;
        internal const byte DW_CFA_val_offset = 0x14;
        internal const byte DW_CFA_val_offset_sf = 0x15;
        internal const byte DW_CFA_val_expression = 0x16;
        internal const byte DW_CFA_advance_loc = 0x40;
        internal const byte DW_CFA_offset = 0x80;
        internal const byte DW_CFA_restore = 0xC0;
        internal const byte DW_CFA_GNU_window_save = 0x2D;
        internal const byte DW_CFA_GNU_args_size = 0x2E;
        internal const byte DW_CFA_GNU_negative_offset_extended = 0x2F;
        internal const byte DW_CFA_AARCH64_negate_ra_state = 0x2D;

        // ===================================================================
        // DW_OP expression opcodes — reader-only (not in DwarfNative)
        // ===================================================================

        internal const byte DW_OP_addr = 0x03;
        internal const byte DW_OP_deref = 0x06;
        internal const byte DW_OP_const1u = 0x08;
        internal const byte DW_OP_const1s = 0x09;
        internal const byte DW_OP_const2u = 0x0A;
        internal const byte DW_OP_const2s = 0x0B;
        internal const byte DW_OP_const4u = 0x0C;
        internal const byte DW_OP_const4s = 0x0D;
        internal const byte DW_OP_const8u = 0x0E;
        internal const byte DW_OP_const8s = 0x0F;
        internal const byte DW_OP_constu = 0x10;
        internal const byte DW_OP_consts = 0x11;
        internal const byte DW_OP_dup = 0x12;
        internal const byte DW_OP_drop = 0x13;
        internal const byte DW_OP_over = 0x14;
        internal const byte DW_OP_pick = 0x15;
        internal const byte DW_OP_swap = 0x16;
        internal const byte DW_OP_rot = 0x17;
        internal const byte DW_OP_xderef = 0x18;
        internal const byte DW_OP_abs = 0x19;
        internal const byte DW_OP_and = 0x1A;
        internal const byte DW_OP_div = 0x1B;
        internal const byte DW_OP_minus = 0x1C;
        internal const byte DW_OP_mod = 0x1D;
        internal const byte DW_OP_mul = 0x1E;
        internal const byte DW_OP_neg = 0x1F;
        internal const byte DW_OP_not = 0x20;
        internal const byte DW_OP_or = 0x21;
        internal const byte DW_OP_plus = 0x22;
        internal const byte DW_OP_plus_uconst = 0x23;
        internal const byte DW_OP_shl = 0x24;
        internal const byte DW_OP_shr = 0x25;
        internal const byte DW_OP_shra = 0x26;
        internal const byte DW_OP_xor = 0x27;
        internal const byte DW_OP_skip = 0x2F;
        internal const byte DW_OP_bra = 0x28;
        internal const byte DW_OP_eq = 0x29;
        internal const byte DW_OP_ge = 0x2A;
        internal const byte DW_OP_gt = 0x2B;
        internal const byte DW_OP_le = 0x2C;
        internal const byte DW_OP_lt = 0x2D;
        internal const byte DW_OP_ne = 0x2E;

        // DW_OP_lit0 .. DW_OP_lit31 = 0x30 .. 0x4F
        internal const byte DW_OP_lit0 = 0x30;

        // DW_OP_reg0 .. DW_OP_reg31 = 0x50 .. 0x6F
        internal const byte DW_OP_reg0 = 0x50;

        // DW_OP_breg0 .. DW_OP_breg31 = 0x70 .. 0x8F
        internal const byte DW_OP_breg0 = 0x70;

        internal const byte DW_OP_regx = 0x90;
        internal const byte DW_OP_fbreg = 0x91;
        internal const byte DW_OP_bregx = 0x92;
        internal const byte DW_OP_piece = 0x93;
        internal const byte DW_OP_deref_size = 0x94;
        internal const byte DW_OP_xderef_size = 0x95;
        internal const byte DW_OP_nop = 0x96;

        // ===================================================================
        // Reader-only limits
        // ===================================================================

        // Maximum register number for PrologInfo savedRegisters array.
        // This matches libunwind's kMaxRegisterNumber for x86_64 (96).
        // Other architectures use fewer but we use a single maximum for simplicity.
        internal const int MaxRegisterNumber = 96;

        // Maximum depth for DW_CFA_remember_state stack
        internal const int MaxRememberStateDepth = 8;

        // Maximum DWARF expression evaluation stack depth
        internal const int MaxExpressionStackDepth = 64;
    }

    /// <summary>
    /// How a register value is recovered during unwinding.
    /// Matches libunwind's RegisterLocation::Location enum.
    /// </summary>
    internal enum DwarfRegisterLocationKind : byte
    {
        Unused = 0,
        Undefined = 1,
        InCFA = 2,            // value at memory[CFA + offset]
        OffsetFromCFA = 3,    // value = CFA + offset
        InRegister = 4,       // value = contents of another register
        AtExpression = 5,     // value at memory[evaluate(expression)]
        IsExpression = 6,     // value = evaluate(expression)
    }
}
