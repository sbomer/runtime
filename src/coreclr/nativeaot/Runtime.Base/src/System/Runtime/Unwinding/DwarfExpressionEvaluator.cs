// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Evaluates DWARF expressions (DW_OP_* opcodes) used by
    /// DW_CFA_expression, DW_CFA_val_expression, and DW_CFA_def_cfa_expression.
    /// </summary>
    internal static unsafe class DwarfExpressionEvaluator
    {
        /// <summary>
        /// Evaluate a DWARF expression and return the result.
        /// The expression pointer points to the ULEB128-encoded length followed by the expression bytes.
        /// </summary>
        /// <param name="expressionAddr">Pointer to the ULEB128 length prefix of the expression.</param>
        /// <param name="initialValue">Initial value pushed onto the stack (typically the CFA).</param>
        /// <param name="getRegister">Function pointer to read a register value by DWARF number.</param>
        /// <param name="regContext">Opaque context passed to getRegister.</param>
        /// <param name="result">The result of the expression evaluation.</param>
        internal static bool TryEvaluate(
            byte* expressionAddr,
            nuint initialValue,
            delegate*<void*, int, nuint> getRegister,
            void* regContext,
            out nuint result)
        {
            result = 0;
            byte* p = expressionAddr;

            // Read expression length
            // Use a generous end bound for the ULEB128 itself
            byte* lengthEnd = p + 10;
            if (!DwarfReader.TryReadULEB128(ref p, lengthEnd, out nuint exprLen))
                return false;

            byte* end = p + exprLen;

            // Expression evaluation stack
            ExpressionStack stack = default;
            int stackTop = -1;

            // Push initial value (CFA or 0)
            stackTop++;
            stack[stackTop] = initialValue;

            while (p < end)
            {
                byte op = *p;
                p++;

                // DW_OP_lit0 .. DW_OP_lit31
                if (op >= DwarfConstants.DW_OP_lit0 && op <= DwarfConstants.DW_OP_lit0 + 31)
                {
                    if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                        return false;
                    stackTop++;
                    stack[stackTop] = (nuint)(op - DwarfConstants.DW_OP_lit0);
                    continue;
                }

                // DW_OP_reg0 .. DW_OP_reg31
                if (op >= DwarfConstants.DW_OP_reg0 && op <= DwarfConstants.DW_OP_reg0 + 31)
                {
                    if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                        return false;
                    int regNum = op - DwarfConstants.DW_OP_reg0;
                    stackTop++;
                    stack[stackTop] = getRegister(regContext, regNum);
                    continue;
                }

                // DW_OP_breg0 .. DW_OP_breg31
                if (op >= DwarfConstants.DW_OP_breg0 && op <= DwarfConstants.DW_OP_breg0 + 31)
                {
                    if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                        return false;
                    if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                        return false;
                    int regNum = op - DwarfConstants.DW_OP_breg0;
                    stackTop++;
                    stack[stackTop] = (nuint)((long)getRegister(regContext, regNum) + offset);
                    continue;
                }

                switch (op)
                {
                    case DwarfConstants.DW_OP_addr:
                        if (!DwarfReader.TryReadPointer(ref p, end, out nuint addr))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = addr;
                        break;

                    case DwarfConstants.DW_OP_deref:
                        if (stackTop < 0)
                            return false;
                        stack[stackTop] = *(nuint*)stack[stackTop];
                        break;

                    case DwarfConstants.DW_OP_deref_size:
                    {
                        if (stackTop < 0)
                            return false;
                        if (!DwarfReader.TryReadByte(ref p, end, out byte size))
                            return false;
                        nuint address = stack[stackTop];
                        nuint val = 0;
                        Unsafe.CopyBlockUnaligned(&val, (void*)address, size);
                        stack[stackTop] = val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const1u:
                    {
                        if (!DwarfReader.TryReadByte(ref p, end, out byte val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const1s:
                    {
                        if (!DwarfReader.TryReadByte(ref p, end, out byte val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)(nint)(sbyte)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const2u:
                    {
                        if (!DwarfReader.TryReadUInt16(ref p, end, out ushort val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const2s:
                    {
                        if (!DwarfReader.TryReadInt16(ref p, end, out short val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)(nint)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const4u:
                    {
                        if (!DwarfReader.TryReadUInt32(ref p, end, out uint val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const4s:
                    {
                        if (!DwarfReader.TryReadInt32(ref p, end, out int val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)(nint)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const8u:
                    {
                        if (!DwarfReader.TryReadUInt64(ref p, end, out ulong val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_const8s:
                    {
                        if (!DwarfReader.TryReadInt64(ref p, end, out long val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)(nint)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_constu:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = val;
                        break;
                    }

                    case DwarfConstants.DW_OP_consts:
                    {
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long val))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)(nint)val;
                        break;
                    }

                    case DwarfConstants.DW_OP_dup:
                        if (stackTop < 0 || stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stack[stackTop + 1] = stack[stackTop];
                        stackTop++;
                        break;

                    case DwarfConstants.DW_OP_drop:
                        if (stackTop < 0)
                            return false;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_over:
                        if (stackTop < 1 || stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stack[stackTop + 1] = stack[stackTop - 1];
                        stackTop++;
                        break;

                    case DwarfConstants.DW_OP_pick:
                    {
                        if (!DwarfReader.TryReadByte(ref p, end, out byte idx))
                            return false;
                        if (idx > stackTop || stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stack[stackTop + 1] = stack[stackTop - idx];
                        stackTop++;
                        break;
                    }

                    case DwarfConstants.DW_OP_swap:
                    {
                        if (stackTop < 1)
                            return false;
                        nuint tmp = stack[stackTop];
                        stack[stackTop] = stack[stackTop - 1];
                        stack[stackTop - 1] = tmp;
                        break;
                    }

                    case DwarfConstants.DW_OP_rot:
                    {
                        if (stackTop < 2)
                            return false;
                        nuint top = stack[stackTop];
                        stack[stackTop] = stack[stackTop - 1];
                        stack[stackTop - 1] = stack[stackTop - 2];
                        stack[stackTop - 2] = top;
                        break;
                    }

                    // Arithmetic/logic operations
                    case DwarfConstants.DW_OP_abs:
                        if (stackTop < 0)
                            return false;
                        if ((long)stack[stackTop] < 0)
                            stack[stackTop] = (nuint)(-(long)stack[stackTop]);
                        break;

                    case DwarfConstants.DW_OP_and:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = stack[stackTop - 1] & stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_div:
                        if (stackTop < 1) return false;
                        if ((long)stack[stackTop] == 0) return false;
                        stack[stackTop - 1] = (nuint)((long)stack[stackTop - 1] / (long)stack[stackTop]);
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_minus:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] -= stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_mod:
                        if (stackTop < 1) return false;
                        if (stack[stackTop] == 0) return false;
                        stack[stackTop - 1] %= stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_mul:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] *= stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_neg:
                        if (stackTop < 0) return false;
                        stack[stackTop] = (nuint)(-(long)stack[stackTop]);
                        break;

                    case DwarfConstants.DW_OP_not:
                        if (stackTop < 0) return false;
                        stack[stackTop] = ~stack[stackTop];
                        break;

                    case DwarfConstants.DW_OP_or:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] |= stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_plus:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] += stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_plus_uconst:
                    {
                        if (stackTop < 0) return false;
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint val))
                            return false;
                        stack[stackTop] += val;
                        break;
                    }

                    case DwarfConstants.DW_OP_shl:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] <<= (int)stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_shr:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] >>= (int)stack[stackTop];
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_shra:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = (nuint)((long)stack[stackTop - 1] >> (int)stack[stackTop]);
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_xor:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] ^= stack[stackTop];
                        stackTop--;
                        break;

                    // Comparison operations (push 1 for true, 0 for false)
                    case DwarfConstants.DW_OP_eq:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = stack[stackTop - 1] == stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_ge:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = (long)stack[stackTop - 1] >= (long)stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_gt:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = (long)stack[stackTop - 1] > (long)stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_le:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = (long)stack[stackTop - 1] <= (long)stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_lt:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = (long)stack[stackTop - 1] < (long)stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    case DwarfConstants.DW_OP_ne:
                        if (stackTop < 1) return false;
                        stack[stackTop - 1] = stack[stackTop - 1] != stack[stackTop] ? (nuint)1 : 0;
                        stackTop--;
                        break;

                    // Control flow
                    case DwarfConstants.DW_OP_bra:
                    {
                        if (stackTop < 0) return false;
                        if (!DwarfReader.TryReadInt16(ref p, end, out short offset))
                            return false;
                        nuint condition = stack[stackTop];
                        stackTop--;
                        if (condition != 0)
                            p += offset;
                        break;
                    }

                    case DwarfConstants.DW_OP_skip:
                    {
                        if (!DwarfReader.TryReadInt16(ref p, end, out short offset))
                            return false;
                        p += offset;
                        break;
                    }

                    case DwarfConstants.DW_OP_regx:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint regNum))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = getRegister(regContext, (int)regNum);
                        break;
                    }

                    case DwarfConstants.DW_OP_bregx:
                    {
                        if (!DwarfReader.TryReadULEB128(ref p, end, out nuint regNum))
                            return false;
                        if (!DwarfReader.TryReadSLEB128(ref p, end, out long offset))
                            return false;
                        if (stackTop >= DwarfConstants.MaxExpressionStackDepth - 1)
                            return false;
                        stackTop++;
                        stack[stackTop] = (nuint)((long)getRegister(regContext, (int)regNum) + offset);
                        break;
                    }

                    case DwarfConstants.DW_OP_nop:
                        break;

                    default:
                        return false; // Unsupported opcode
                }
            }

            if (stackTop < 0)
                return false;

            result = stack[stackTop];
            return true;
        }

        [InlineArray(DwarfConstants.MaxExpressionStackDepth)]
        private struct ExpressionStack
        {
            private nuint _element0;
        }
    }
}
