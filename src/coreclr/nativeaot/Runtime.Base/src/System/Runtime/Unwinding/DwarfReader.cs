// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Low-level readers for DWARF-encoded data from raw memory.
    /// All methods take a ref pointer that is advanced past the read data,
    /// and an end pointer for bounds checking.
    /// </summary>
    internal static unsafe class DwarfReader
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadByte(ref byte* p, byte* end, out byte value)
        {
            if (p >= end)
            {
                value = 0;
                return false;
            }
            value = *p;
            p++;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadUInt16(ref byte* p, byte* end, out ushort value)
        {
            if (p + 2 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<ushort>(p);
            p += 2;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadUInt32(ref byte* p, byte* end, out uint value)
        {
            if (p + 4 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<uint>(p);
            p += 4;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadUInt64(ref byte* p, byte* end, out ulong value)
        {
            if (p + 8 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<ulong>(p);
            p += 8;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadInt16(ref byte* p, byte* end, out short value)
        {
            if (p + 2 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<short>(p);
            p += 2;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadInt32(ref byte* p, byte* end, out int value)
        {
            if (p + 4 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<int>(p);
            p += 4;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadInt64(ref byte* p, byte* end, out long value)
        {
            if (p + 8 > end)
            {
                value = 0;
                return false;
            }
            value = Unsafe.ReadUnaligned<long>(p);
            p += 8;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryReadPointer(ref byte* p, byte* end, out nuint value)
        {
            if (sizeof(nuint) == 8)
            {
                if (!TryReadUInt64(ref p, end, out ulong v))
                {
                    value = 0;
                    return false;
                }
                value = (nuint)v;
            }
            else
            {
                if (!TryReadUInt32(ref p, end, out uint v))
                {
                    value = 0;
                    return false;
                }
                value = (nuint)v;
            }
            return true;
        }

        /// <summary>
        /// Read an unsigned LEB128 value. Returns false if the data is truncated
        /// or the value overflows a nuint.
        /// </summary>
        internal static bool TryReadULEB128(ref byte* p, byte* end, out nuint result)
        {
            result = 0;
            int shift = 0;

            while (p < end)
            {
                byte b = *p;
                p++;

                result |= ((nuint)(b & 0x7F)) << shift;
                shift += 7;

                if ((b & 0x80) == 0)
                    return true;

                // Prevent overflow: nuint is at most 64 bits
                if (shift >= sizeof(nuint) * 8)
                {
                    result = 0;
                    return false;
                }
            }

            // Truncated
            result = 0;
            return false;
        }

        /// <summary>
        /// Read a signed LEB128 value.
        /// </summary>
        internal static bool TryReadSLEB128(ref byte* p, byte* end, out long result)
        {
            result = 0;
            int shift = 0;
            byte b = 0;

            while (p < end)
            {
                b = *p;
                p++;

                result |= ((long)(b & 0x7F)) << shift;
                shift += 7;

                if ((b & 0x80) == 0)
                    break;

                if (shift >= 64)
                {
                    result = 0;
                    return false;
                }
            }

            // Sign extend if the high bit of the last byte was set
            if (shift < 64 && (b & 0x40) != 0)
                result |= -(1L << shift);

            return true;
        }

        /// <summary>
        /// Read a DWARF-encoded pointer value using DW_EH_PE_* encoding.
        /// </summary>
        /// <param name="p">Current read position, advanced on success.</param>
        /// <param name="end">End of readable data.</param>
        /// <param name="encoding">The DW_EH_PE_* encoding byte.</param>
        /// <param name="ehFrameHdrStart">Base for DW_EH_PE_datarel.</param>
        /// <param name="textSegmentBase">Base for DW_EH_PE_textrel.</param>
        /// <param name="funcStart">Base for DW_EH_PE_funcrel.</param>
        /// <param name="result">The decoded pointer value.</param>
        internal static bool TryReadEncodedPointer(
            ref byte* p, byte* end,
            byte encoding,
            nuint ehFrameHdrStart,
            nuint textSegmentBase,
            nuint funcStart,
            out nuint result)
        {
            result = 0;

            if (encoding == DwarfConstants.DW_EH_PE_omit)
                return true;

            byte* startAddr = p;

            // Read the base value according to the format (low 4 bits)
            long baseValue;
            switch (encoding & DwarfConstants.DW_EH_PE_FORMAT_MASK)
            {
                case DwarfConstants.DW_EH_PE_absptr:
                    if (!TryReadPointer(ref p, end, out nuint absVal))
                        return false;
                    baseValue = (long)absVal;
                    break;
                case DwarfConstants.DW_EH_PE_uleb128:
                    if (!TryReadULEB128(ref p, end, out nuint ulebVal))
                        return false;
                    baseValue = (long)ulebVal;
                    break;
                case DwarfConstants.DW_EH_PE_sleb128:
                    if (!TryReadSLEB128(ref p, end, out long slebVal))
                        return false;
                    baseValue = slebVal;
                    break;
                case DwarfConstants.DW_EH_PE_udata2:
                    if (!TryReadUInt16(ref p, end, out ushort u16))
                        return false;
                    baseValue = u16;
                    break;
                case DwarfConstants.DW_EH_PE_udata4:
                    if (!TryReadUInt32(ref p, end, out uint u32))
                        return false;
                    baseValue = u32;
                    break;
                case DwarfConstants.DW_EH_PE_udata8:
                    if (!TryReadUInt64(ref p, end, out ulong u64))
                        return false;
                    baseValue = (long)u64;
                    break;
                case DwarfConstants.DW_EH_PE_sdata2:
                    if (!TryReadInt16(ref p, end, out short s16))
                        return false;
                    baseValue = s16;
                    break;
                case DwarfConstants.DW_EH_PE_sdata4:
                    if (!TryReadInt32(ref p, end, out int s32))
                        return false;
                    baseValue = s32;
                    break;
                case DwarfConstants.DW_EH_PE_sdata8:
                    if (!TryReadInt64(ref p, end, out long s64))
                        return false;
                    baseValue = s64;
                    break;
                default:
                    return false; // Unsupported format
            }

            // Apply the application (high 4 bits minus indirect flag)
            switch (encoding & DwarfConstants.DW_EH_PE_APPL_MASK)
            {
                case 0: // DW_EH_PE_absptr application — no adjustment
                    break;
                case DwarfConstants.DW_EH_PE_pcrel:
                    baseValue += (long)(nint)startAddr;
                    break;
                case DwarfConstants.DW_EH_PE_textrel:
                    baseValue += (long)textSegmentBase;
                    break;
                case DwarfConstants.DW_EH_PE_datarel:
                    baseValue += (long)ehFrameHdrStart;
                    break;
                case DwarfConstants.DW_EH_PE_funcrel:
                    baseValue += (long)funcStart;
                    break;
                default:
                    return false; // Unsupported application
            }

            // Handle indirect (pointer to pointer)
            if ((encoding & DwarfConstants.DW_EH_PE_indirect) != 0)
            {
                baseValue = (long)*(nuint*)(nint)baseValue;
            }

            result = (nuint)baseValue;
            return true;
        }
    }
}
