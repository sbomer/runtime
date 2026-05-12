// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime.Unwinding
{
    /// <summary>
    /// Managed entry points for DWARF-based stack unwinding, exported to the
    /// native NativeAOT runtime via [RuntimeExport].
    ///
    /// These replace the C++ UnwindHelpers::StepFrame and
    /// UnwindHelpers::GetUnwindProcInfo functions. The native
    /// UnixNativeCodeManager calls these through function pointers.
    /// </summary>
    internal static unsafe class ManagedUnwindHelpers
    {
        /// <summary>
        /// Unwind a single stack frame using DWARF CFI or compact unwind data.
        ///
        /// Equivalent to the native UnwindHelpers::StepFrame.
        ///
        /// Returns 1 on success, 0 on failure (avoids bool ABI ambiguity).
        /// </summary>
        /// <param name="regs">Pointer to the REGDISPLAY to update.</param>
        /// <param name="startIp">Function start IP (from unw_proc_info_t).</param>
        /// <param name="format">Compact unwind encoding (0 for pure DWARF).</param>
        /// <param name="unwindInfo">Pointer to FDE in .eh_frame (for DWARF).</param>
        /// <param name="ehFrame">Base of the .eh_frame section.</param>
        /// <param name="ehFrameLength">Length of the .eh_frame section.</param>
        [RuntimeExport("ManagedStepFrame")]
        public static int StepFrame(
            DwarfStepper.RegDisplay* regs,
            nuint startIp,
            uint format,
            nuint unwindInfo,
            nuint ehFrame,
            nuint ehFrameLength)
        {
            // Try compact unwind first (Apple platforms only; stub on Linux)
            if (format != 0 && !CompactUnwindConstants.IsDwarfEncoding(format))
            {
                if (CompactUnwindDecoder.TryStep(regs, format, startIp))
                    return 1;
                return 0;
            }

            // Fall back to DWARF
            nuint pc = regs->IP;
            if (DwarfStepper.TryStepWithDwarf(regs, unwindInfo, ehFrame, ehFrameLength, pc))
                return 1;

            return 0;
        }

        /// <summary>
        /// Find unwind information for a given PC from the .eh_frame_hdr
        /// and .eh_frame sections.
        ///
        /// Equivalent to the native UnwindHelpers::GetUnwindProcInfo, but only
        /// the DWARF lookup portion. Compact unwind section lookup remains in
        /// native code.
        ///
        /// Returns 1 on success, 0 on failure.
        /// </summary>
        /// <param name="pc">The instruction pointer to look up.</param>
        /// <param name="sections">Pointer to the native UnwindInfoSections.</param>
        /// <param name="procInfo">Output: the unwind procedure info.</param>
        [RuntimeExport("ManagedGetUnwindProcInfo")]
        public static int GetUnwindProcInfo(
            nuint pc,
            UnwindInfoSections* sections,
            UnwindProcInfo* procInfo)
        {
            *procInfo = default;

            // Try .eh_frame_hdr binary search first (fast path)
            if (sections->DwarfIndexSection != 0 && sections->DwarfIndexSectionLength != 0)
            {
                if (DwarfEhFrame.TryFindFdeFromEhFrameHdr(
                    (byte*)sections->DwarfIndexSection,
                    sections->DwarfIndexSectionLength,
                    pc,
                    (byte*)sections->DwarfSection,
                    sections->DwarfSectionLength,
                    out DwarfFdeInfo fde, out DwarfCieInfo cie))
                {
                    FillProcInfo(procInfo, ref fde, ref cie);
                    return 1;
                }
            }

            // Fall back to linear .eh_frame scan
            if (sections->DwarfSection != 0 && sections->DwarfSectionLength != 0)
            {
                if (DwarfEhFrame.TryFindFdeFromEhFrame(
                    (byte*)sections->DwarfSection,
                    sections->DwarfSectionLength,
                    pc,
                    out DwarfFdeInfo fde, out DwarfCieInfo cie))
                {
                    FillProcInfo(procInfo, ref fde, ref cie);
                    return 1;
                }
            }

            return 0;
        }

        private static void FillProcInfo(
            UnwindProcInfo* procInfo,
            ref DwarfFdeInfo fde,
            ref DwarfCieInfo cie)
        {
            procInfo->StartIp = fde.PcStart;
            procInfo->EndIp = fde.PcEnd;
            procInfo->Lsda = fde.Lsda;
            procInfo->Handler = cie.Personality;
            procInfo->Format = 0; // DWARF (not compact)
            procInfo->UnwindInfo = fde.FdeStart;
        }
    }
}
