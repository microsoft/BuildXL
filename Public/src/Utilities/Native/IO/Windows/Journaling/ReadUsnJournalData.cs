// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Request structure indicating expected journal identifier, start USN, etc.
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/hh802706(v=vs.85).aspx
    /// </summary>
    /// <remarks>
    /// We use the V1 rather than V0 structure even before 8.1 / Server 2012 R2 (it is just like
    /// the V0 version but with the version range fields at the end).
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct ReadUsnJournalData
    {
        /// <summary>
        /// Size of this structure (there are no variable length fields).
        /// </summary>
        public static readonly int Size = Marshal.SizeOf<ReadUsnJournalData>();

        /// <nodoc />
        public Usn StartUsn;

        /// <nodoc />
        public uint ReasonMask;

        /// <nodoc />
        public uint ReturnOnlyOnClose;

        /// <nodoc />
        public ulong Timeout;

        /// <nodoc />
        public ulong BytesToWaitFor;

        /// <nodoc />
        public ulong UsnJournalID;

        /// <nodoc />
        public ushort MinMajorVersion;

        /// <nodoc />
        public ushort MaxMajorVersion;
    }
}
