// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Text;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Used as the return type from AddOrGet() to return a "race winner"
    /// in case the Add was rejected due to not matching a prior record
    /// in the cache.  It is a class such that it may be NULL for cases
    /// where the cache accepted the Add.
    /// </summary>
    [EventData]
    public sealed class FullCacheRecord : IEquatable<FullCacheRecord>
    {
        private readonly CasEntries m_hashes;
        private readonly StrongFingerprint m_strongFingerprint;

        /// <summary>
        /// The strong fingerprint
        /// </summary>
        [EventField]
        public StrongFingerprint StrongFingerprint => m_strongFingerprint;

        /// <summary>
        /// The cacheId from which the records was retrived
        /// </summary>
        [EventField]
        public string CacheId => m_strongFingerprint.CacheId;

        /// <summary>
        /// The associated entries in the CAS for this cache record.
        /// </summary>
        [EventField]
        public CasEntries CasEntries => m_hashes;

        /// <summary>
        /// Constructor of a Cache record
        /// </summary>
        /// <param name="strong">The strong fingerprint</param>
        /// <param name="hashes">The set of CasHash hashes</param>
        public FullCacheRecord(StrongFingerprint strong, CasEntries hashes)
        {
            Contract.Requires(strong != null);
            Contract.Requires(hashes.IsValid);

            m_strongFingerprint = strong;
            m_hashes = hashes;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            StringBuilder text = new StringBuilder(m_strongFingerprint.ToString());
            text.Append(" => ");
            text.Append(m_hashes.Determinism);
            text.Append(" Cas[");
            text.Append(m_hashes.Count);
            text.Append("]={");

            for (int i = 0; i < m_hashes.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(",");
                }

                text.Append(m_hashes[i]);
            }

            text.Append("}");

            return text.ToString();
        }

        /// <summary>
        /// Compare two FullCacheRecords
        /// </summary>
        /// <param name="other">The other full cache record to compare with</param>
        /// <returns>True if the StrongFingerprint and CasHash array contents are the same</returns>
        /// <remarks>
        /// StrongFingerprints are only compared based on their three hash values.  This
        /// is no different for cache record comparison.
        /// </remarks>
        public bool Equals(FullCacheRecord other)
        {
            return !object.ReferenceEquals(other, null) &&
                StrongFingerprint.Equals(other.StrongFingerprint) &&
                m_hashes.Equals(other.m_hashes);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            // If obj is not a StrongFingerprint is becomes null
            // and will not match a non-null.  Handle all of that in
            // the type-specific Equals.
            return Equals(obj as FullCacheRecord);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // The strong fingerprint should be good enough
            // for the hash code.
            return m_strongFingerprint.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(FullCacheRecord left, FullCacheRecord right)
        {
            // We need to handle NULL special since we can
            // not just call Equals method on NULL
            // Thus, we first do reference equals (easy/fast)
            // and then the NULL check for left and then
            // finally go to the Equals() method.
            if (object.ReferenceEquals(left, right))
            {
                return true;
            }

            if (object.ReferenceEquals(left, null))
            {
                return false;
            }

            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FullCacheRecord left, FullCacheRecord right)
        {
            // We need to handle NULL special since we can
            // not just call Equals method on NULL
            // Thus, we first do reference equals (easy/fast)
            // and then the NULL check for left and then
            // finally go to the Equals() method.
            if (object.ReferenceEquals(left, right))
            {
                return false;
            }

            if (object.ReferenceEquals(left, null))
            {
                return true;
            }

            return !left.Equals(right);
        }
    }
}
