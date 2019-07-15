// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Represents a wrapper for the FullCacheRecord so that clients can receive
    /// determinism values for successful addorget calls with a null FullCacheRecord.
    /// </summary>
    [EventData]
    public readonly struct FullCacheRecordWithDeterminism : IEquatable<FullCacheRecordWithDeterminism>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="FullCacheRecordWithDeterminism"/> struct.
        /// </summary>
        public FullCacheRecordWithDeterminism(FullCacheRecord record)
        {
            Contract.Requires(record != null);
            Record = record;
            Determinism = record.CasEntries.Determinism;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="FullCacheRecordWithDeterminism"/> struct.
        /// </summary>
        public FullCacheRecordWithDeterminism(CacheDeterminism determinism)
        {
            Record = null;
            Determinism = determinism;
        }

        /// <summary>
        /// The full cache record for the object. Can be null.
        /// </summary>
        [EventField]
        public FullCacheRecord Record { get; }

        /// <summary>
        /// The determinism associated with this full cache record entry.
        /// </summary>
        // Use public field to avoid copy operation for property access.
        public readonly CacheDeterminism Determinism;

        [EventField]
        private CacheDeterminism CacheDeterminism => Determinism;

        /// <inheritdoc />
        public bool Equals(FullCacheRecordWithDeterminism other)
        {
            if (!ReferenceEquals(Record, other.Record))
            {
                if (Record == null || other.Record == null)
                {
                    return false;
                }

                if (!Record.Equals(other.Record))
                {
                    return false;
                }
            }

            return Determinism.Equals(other.Determinism);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Record?.GetHashCode() ?? 0) ^ Determinism.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(FullCacheRecordWithDeterminism left, FullCacheRecordWithDeterminism right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FullCacheRecordWithDeterminism left, FullCacheRecordWithDeterminism right)
        {
            return !left.Equals(right);
        }
    }
}
