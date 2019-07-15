// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Contains information about the set of CasEntries associated with a strong fingerprint
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710", Justification = "This is not a plain collection")]
    [EventData]
    public readonly struct CasEntries : IEnumerable<CasHash>, IEquatable<CasEntries>
    {
        /// <summary>
        /// This array of CAS Hashes must have its order preserved
        /// as the order is significant.  It may also have duplicates
        /// in the array if two of the files happen to have the same
        /// content (for example an "empty" file)
        ///
        /// All APIs that have CasHash arrays depend on order and
        /// thus must preserve order.
        /// </summary>
        private readonly CasHash[] m_hashes;

        /// <summary>
        /// The CacheDeterminism information for these entries
        /// </summary>
        public readonly CacheDeterminism Determinism;

        /// <summary>
        /// Construct a CasHash entries read-only array from the given array
        /// </summary>
        /// <param name="casHashes">The ordred list of CasHashes</param>
        /// <param name="determinism">The CacheDeterminism value for this CasEntries</param>
        /// <remarks>
        /// The isDeterministic flag defaults to CacheDeterminism.None but if the
        /// buildengine knows that the content produced by the build transform
        /// is deterministic, it can set this to CacheDeterminism.Tool to help
        /// (a) the cache operate better and
        /// (b) catch bugs where determinism was not achieved even when expected
        ///
        /// Cache Agregators may use this flag in order to flag a "deterministic"
        /// result that was achieved via a shared cache (cache based determinism
        /// recovery) and will also help catch failures of that to work correctly.
        /// </remarks>
        public CasEntries(IEnumerable<CasHash> casHashes, CacheDeterminism determinism = default(CacheDeterminism))
        {
            Contract.Requires(casHashes != null);

            // The extension method here does the most efficient copy it can
            m_hashes = casHashes.ToArray();
            Determinism = determinism;
        }

        /// <summary>
        /// Construct a CasEntries from a another CanEntries without copying the
        /// array since both are read-only.
        /// </summary>
        /// <param name="casEntries">The source CasEntries</param>
        /// <param name="determinism">The new determinism flag</param>
        /// <remarks>
        /// This is for taking a CasEntries item and producing a new
        /// one with a different deterministic setting
        /// </remarks>
        public CasEntries(CasEntries casEntries, CacheDeterminism determinism = default(CacheDeterminism))
        {
            Contract.Requires(casEntries.IsValid);

            m_hashes = casEntries.m_hashes;
            Determinism = determinism;
        }

        [EventField]
        private CacheDeterminism CacheDeterminism => Determinism;

        /// <summary>
        /// Since it is a struct we can not prevent default&lt;CasEntries&gt;
        /// but we can detect it because it would have a null CasHash array
        /// which would not be valid.
        /// </summary>
        [EventIgnore]
        public bool IsValid => m_hashes != null;

        /// <summary>
        /// Number of CasHash entries for this cache record
        /// </summary>
        [EventField]
        public int Count => m_hashes.Length;

        /// <summary>
        /// Return the CasHash at the zero-based index of
        /// the CasHash array in the record
        /// </summary>
        /// <param name="index">Zero based index into the CasHash array</param>
        /// <returns>
        /// The CasHash at the given index in this cache record
        /// </returns>
        /// <remarks>
        /// The reason we don't just return the array is that there
        /// is nothing in C# that prevents the consumer from mutating
        /// it an thus changing the record itself.
        /// </remarks>
        public ref readonly CasHash this[int index] => ref m_hashes[index];

        /// <inheritdoc/>
        public IEnumerator<CasHash> GetEnumerator()
        {
            return ((IEnumerable<CasHash>)m_hashes).GetEnumerator();
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<CasHash>)m_hashes).GetEnumerator();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Compare our Cas hashes to the CasHash array given
        /// </summary>
        /// <param name="hashes">Array of CasHashes</param>
        /// <returns>True if the CasHash array matches this CasEntries</returns>
        /// <remarks>
        /// Note that matches are exact, including order since
        /// order is significant.  Any deviation is not a match.
        /// </remarks>
        public bool Equals(CasHash[] hashes)
        {
            // Just in case the array is the same instance
            if (m_hashes == hashes)
            {
                return true;
            }

            if ((hashes == null) || (m_hashes.Length != hashes.Length))
            {
                return false;
            }

            for (int i = 0; i < m_hashes.Length; i++)
            {
                if (!m_hashes[i].Equals(hashes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <nodoc />
        public bool Equals(CasEntries other)
        {
            return Equals(other.m_hashes);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            int hash = 0;
            foreach (CasHash cas in m_hashes)
            {
                hash ^= cas.GetHashCode();
            }

            return hash;
        }

        /// <nodoc />
        public static bool operator ==(CasEntries left, CasEntries right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CasEntries left, CasEntries right)
        {
            return !left.Equals(right);
        }

        // These are here such that we can compare a CasHash array with a
        // CasEntries item without first making a CasEntries item.

        /// <nodoc />
        public static bool operator ==(CasEntries left, CasHash[] right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator ==(CasHash[] left, CasEntries right)
        {
            return right.Equals(left);
        }

        /// <nodoc />
        public static bool operator !=(CasEntries left, CasHash[] right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CasHash[] left, CasEntries right)
        {
            return !right.Equals(left);
        }

        /// <summary>
        /// Create a CasEntries item from the given CasHashes
        /// </summary>
        /// <param name="casHashes">The array of CasHash items to convert</param>
        /// <returns>
        /// A CasEntries set that has the default determinism (none)
        /// </returns>
        public static CasEntries FromCasHashes(params CasHash[] casHashes)
        {
            // This function was "required" from FxCop due to the implicit
            // operator that can convert a CasHash[] to CasEntries
            return new CasEntries((IEnumerable<CasHash>)casHashes);
        }

        /// <summary>
        /// Implicity conversion from CasHash array to CasEntries to make
        /// various APIs easier to work with at the client.
        /// </summary>
        /// <param name="casHashes">The array of CasHash items</param>
        public static implicit operator CasEntries(CasHash[] casHashes)
        {
            // The typecast here is such that C# binds this correctly.
            return FromCasHashes(casHashes);
        }
    }
}
