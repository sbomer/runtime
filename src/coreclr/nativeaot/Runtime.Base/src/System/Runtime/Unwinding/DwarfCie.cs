// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Parses CIE (Common Information Entry) records from .eh_frame sections.
    /// This is the reader-side counterpart of the ObjectWriter's DwarfCie,
    /// which constructs CIE records during compilation.
    /// </summary>
    internal static unsafe class DwarfCieParser
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
    }
}
