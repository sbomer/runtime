// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Searches .eh_frame and .eh_frame_hdr sections to locate FDE records
    /// for a given PC. This is the reader-side counterpart of the ObjectWriter's
    /// DwarfEhFrame, which builds these sections during compilation.
    /// </summary>
    internal static unsafe class DwarfEhFrame
    {
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

            if (!DwarfFdeParser.TryParseFde(fdeLoc, ehFrame, ehFrameEnd, out fde, out cie))
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
                if (DwarfFdeParser.TryParseFde(recordStart, ehFrame, end, out DwarfFdeInfo candidateFde, out DwarfCieInfo candidateCie))
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
    }
}
