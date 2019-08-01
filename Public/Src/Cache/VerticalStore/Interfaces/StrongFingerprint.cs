// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Diagnostics.Tracing;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// This is the strong fingerprint - which is the combination of the
    /// weak fingerprint, the Cas element, and a hash element.
    /// </summary>
    /// <remarks>
    /// This class is meant to be subclassed for carrying any extra information
    /// or state that may be needed by a given cache implementation.  It
    /// can also contain a unique cache identifier for logging/telemetry
    /// purposes.
    ///
    /// Comparison of these objects must only take into account the
    /// 3 hashes (the weak fingerprint, cas element, and hash element) as
    /// that is what defines the strong fingerprint.  Any other comparison
    /// or information is hidden information.
    ///
    /// Note that it really should be immutable, so don't add anything that
    /// can be mutated.
    /// </remarks>
    [EventData]
    public class StrongFingerprint : IEquatable<StrongFingerprint>
    {
        /// <summary>
        /// The weak fingerprint element of this strong fingerprint.
        /// </summary>
        [EventField]
        public WeakFingerprintHash WeakFingerprint => m_weakFingerprint;

        private readonly WeakFingerprintHash m_weakFingerprint;

        /// <summary>
        /// This is the CasHash of some selector information that the
        /// build engine cares about that is stores in the CAS.
        /// It may be a "NoItem" file (different from the empty file)
        /// Exact format is up to the build engine.
        /// </summary>
        /// <remarks>
        /// In the BuildXL case this would be the Input Assertion File List
        /// which contains the list of files (with paths) that make up
        /// the set of files that are accessed within the PIP.
        /// We content that it rarely changes compared to the hashes of the
        /// contents of the files and that it is even shared across not
        /// just multiple builds but multiple PIPs.
        /// </remarks>
        [EventField]
        public CasHash CasElement => m_casElement;

        private readonly CasHash m_casElement;

        /// <summary>
        /// This is a hash in whatever manner and of whatever information
        /// may be needed to produce the unique identifier.
        /// Used as the 3rd part of the 3-part strong fingerprint
        /// </summary>
        /// <remarks>
        /// In the BuildXL case, this would be the hash of the contents of
        /// the files enumerated in the CasElement Input Assertion File List.
        /// This way, if multiple PIPs share the same CasElement, they would
        /// be able to share the same HashElement and thus make for a fast
        /// lookup rather than re-computation.
        /// </remarks>
        [EventField]
        public Hash HashElement => m_hashElement;

        private readonly Hash m_hashElement;

        /// <summary>
        /// The cache that produced this selector should be identified
        /// for tracking/logging of where answers came from.
        ///
        /// It should never be part of the comparison of equality of
        /// strong fingerprints
        /// </summary>
        [EventField]
        public string CacheId => m_cacheId;

        private readonly string m_cacheId;

        /// <summary>
        /// .ctr
        /// </summary>
        /// <param name="weak">Weak Fingerprint Hash of this selector</param>
        /// <param name="casElement">The CAS addressed part of this selector</param>
        /// <param name="hashElement">The basic Hash part of this selector</param>
        /// <param name="cacheId">A string representing this instance of the cache.</param>
        public StrongFingerprint(WeakFingerprintHash weak, CasHash casElement, Hash hashElement, string cacheId)
        {
            Contract.Requires(cacheId != null);

            m_weakFingerprint = weak;
            m_casElement = casElement;
            m_hashElement = hashElement;
            m_cacheId = cacheId;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1}:{2}", WeakFingerprint, CasElement, HashElement);
        }

        /// <summary>
        /// Compare two Strong Fingerprints
        /// </summary>
        /// <param name="other">The other strong fingerprint to compare with</param>
        /// <returns>True if the WeakFingerprint, CasElement, and HashElement are the same</returns>
        /// <remarks>
        /// StrongFingerprints are only compared based on their three hash values.
        /// Ancillary data are not part of this.
        /// </remarks>
        public bool Equals(StrongFingerprint other)
        {
            return (!object.ReferenceEquals(other, null)) &&
                WeakFingerprint.Equals(other.WeakFingerprint) &&
                HashElement.Equals(other.HashElement) &&
                CasElement.Equals(other.CasElement);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            // If object is not a StrongFingerprint is becomes null
            // and will not match a non-null.  Handle all of that in
            // the type-specific Equals.
            return Equals(obj as StrongFingerprint);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return WeakFingerprint.GetHashCode() ^ CasElement.GetHashCode() ^ HashElement.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(StrongFingerprint left, StrongFingerprint right)
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
        public static bool operator !=(StrongFingerprint left, StrongFingerprint right)
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
