// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>I/O counters for both process pips and individual processes.</summary>
    public readonly struct IOCounters
    {
        /// <nodoc/>
        public readonly ulong ReadOperationCount;
        /// <nodoc/>
        public readonly ulong ReadByteCount;
        /// <nodoc/>
        public readonly ulong WriteOperationCount;
        /// <nodoc/>
        public readonly ulong WriteByteCount;
        /// <nodoc/>
        public readonly ulong OtherOperationCount;
        /// <nodoc/>
        public readonly ulong OtherByteCount;

        /// <nodoc/>
        public IOCounters(
            ulong readOpCount,
            ulong readByteCount,
            ulong writeOpCount,
            ulong writeByteCount,
            ulong otherOpCount,
            ulong otherByteCount)
        {
            ReadOperationCount = readOpCount;
            ReadByteCount = readByteCount;
            WriteOperationCount = writeOpCount;
            WriteByteCount = writeByteCount;
            OtherOperationCount = otherOpCount;
            OtherByteCount = otherByteCount;
        }
    }
}
