// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Metadata about a volume's change journal (<c>USN_JOURNAL_DATA_V0</c>).
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa365721(v=vs.85).aspx
    /// </summary>
    /// <remarks>
    /// We always use a V0 structure rather than V1, since V1 just adds a supported USN version range (e.g. 2, 3).
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public sealed class QueryUsnJournalData
    {
        /// <summary>
        /// Size of this structure (there are no variable length fields).
        /// </summary>
        public static readonly int Size = Marshal.SizeOf<QueryUsnJournalData>();

        /// <summary>
        /// Journal identifier which must be used to read the current journal.
        /// </summary>
        /// <remarks>
        /// If this identifier changes, USNs for the old identifier are invalid.
        /// </remarks>
        public ulong UsnJournalId;

        /// <summary>
        /// First USN that can be read from the current journal.
        /// </summary>
        public Usn FirstUsn;

        /// <summary>
        /// Next USN that will be written to the current journal.
        /// </summary>
        public Usn NextUsn;

        /// <summary>
        /// Lowest USN which is valid for the current journal identifier.
        /// </summary>
        /// <remarks>
        /// <see cref="FirstUsn"/> might be higher since the beginning of the
        /// journal may have been truncated (without a new identifier).
        /// </remarks>
        public Usn LowestValidUsn;

        /// <summary>
        /// Max USN after which the journal will have to be fully re-created.
        /// </summary>
        public Usn MaxUsn;

        /// <summary>
        /// Max size after which part of the journal will be truncated.
        /// </summary>
        public ulong MaximumSize;

        /// <summary>
        /// Number of bytes by which the journal extends (and possibly truncates at the beginning).
        /// </summary>
        public ulong AllocationDelta;
    }
}
