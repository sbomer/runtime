// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Parses FDE (Frame Description Entry) records from .eh_frame sections.
    /// This is the reader-side counterpart of the ObjectWriter's DwarfFde,
    /// which constructs FDE records during compilation.
    /// </summary>
    internal static unsafe class DwarfFdeParser
    {
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
            if (!DwarfCieParser.TryParseCie(cieAddr, sectionEnd, out cie))
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
    }
}
