// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Contains I/O accounting information for a process or process tree for a particular type of IO (e.g. read or write).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct IOTypeCounters : IEquatable<IOTypeCounters>
    {
        /// <summary>
        /// Number of operations performed (independent of size).
        /// </summary>
        public readonly ulong OperationCount;

        /// <summary>
        /// Total bytes transferred (regardless of the number of operations used to transfer them).
        /// </summary>
        public readonly ulong TransferCount;

        /// <inheritdoc/>
        public bool Equals(IOTypeCounters other)
        {
            return (OperationCount == other.OperationCount) && (TransferCount == other.TransferCount);
        }

        /// <nodoc />
        public static bool operator !=(IOTypeCounters t1, IOTypeCounters t2)
        {
            return !t1.Equals(t2);
        }

        /// <nodoc />
        public static bool operator ==(IOTypeCounters t1, IOTypeCounters t2)
        {
            return t1.Equals(t2);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return (obj is IOTypeCounters) ? Equals((IOTypeCounters)obj) : false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(OperationCount.GetHashCode(), TransferCount.GetHashCode());
        }

        /// <nodoc />
        public IOTypeCounters(ulong operationCount, ulong transferCount)
        {
            OperationCount = operationCount;
            TransferCount = transferCount;
        }
    }

    /// <summary>
    /// Contains I/O accounting information for a process or process tree.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct IOCounters : IEquatable<IOCounters>
    {
        /// <summary>
        /// Counters for read operations.
        /// </summary>
        public readonly IOTypeCounters ReadCounters;

        /// <summary>
        /// Counters for write operations.
        /// </summary>
        public readonly IOTypeCounters WriteCounters;

        /// <summary>
        /// Counters for other operations (not classified as either read or write).
        /// </summary>
        public readonly IOTypeCounters OtherCounters;

        /// <inheritdoc/>
        public bool Equals(IOCounters other)
        {
            return (ReadCounters == other.ReadCounters) && (WriteCounters == other.WriteCounters) && (OtherCounters == other.OtherCounters);
        }

        /// <nodoc/>
        public static bool operator !=(IOCounters t1, IOCounters t2)
        {
            return !t1.Equals(t2);
        }

        /// <nodoc/>
        public static bool operator ==(IOCounters t1, IOCounters t2)
        {
            return t1.Equals(t2);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return (obj is IOCounters) ? Equals((IOCounters)obj) : false;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(ReadCounters.GetHashCode(), WriteCounters.GetHashCode(), OtherCounters.GetHashCode());
        }

        /// <nodoc />
        public IOCounters(IOTypeCounters readCounters, IOTypeCounters writeCounters, IOTypeCounters otherCounters)
        {
            ReadCounters = readCounters;
            WriteCounters = writeCounters;
            OtherCounters = otherCounters;
        }

        /// <summary>
        /// Computes the aggregate I/O performed (sum of the read, write, and other counters).
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1024:UsePropertiesWhereAppropriate")]
        [Pure]
        public IOTypeCounters GetAggregateIO()
        {
            return new IOTypeCounters(
                operationCount: ReadCounters.OperationCount + WriteCounters.OperationCount + OtherCounters.OperationCount,
                transferCount: ReadCounters.TransferCount + WriteCounters.TransferCount + OtherCounters.TransferCount);
        }
    }
}
