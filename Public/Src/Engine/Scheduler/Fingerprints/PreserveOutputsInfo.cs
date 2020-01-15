// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using StructUtilities = BuildXL.Cache.ContentStore.Interfaces.Utils.StructUtilities;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    ///  Preserve outputs info contains salt(hash) and preserveoutput trust level.
    /// </summary>
    public struct PreserveOutputsInfo : IEquatable<PreserveOutputsInfo>
    {
        /// <summary>
        /// Preserve output trust level
        /// </summary>
        public int PreserveOutputTrustLevel { get; }

        /// <summary>
        /// Content hash
        /// </summary>
        public ContentHash Salt { get; }

        /// <summary>
        /// Instacne of not using preserve outputs info
        /// </summary>
        public static PreserveOutputsInfo PreserveOutputsNotUsed = new PreserveOutputsInfo(WellKnownContentHashes.AbsentFile, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="PreserveOutputsInfo" /> struct from content hash and trust level
        /// </summary>
        public PreserveOutputsInfo(ContentHash preserveOutputSalt, int preserveOutputTrustLevel)
        {
            Salt = preserveOutputSalt;
            PreserveOutputTrustLevel = preserveOutputTrustLevel;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PreserveOutputsInfo" /> struct from buildxl reader
        /// </summary>
        public PreserveOutputsInfo(BuildXLReader reader)
        {
            Salt = new ContentHash(reader);
            PreserveOutputTrustLevel = reader.ReadInt32Compact();
        }

        /// <summary>
        /// Check if is as safe or safer than 
        /// </summary>
        public bool IsAsSafeOrSaferThan(PreserveOutputsInfo other)
        {
            return Salt == other.Salt && PreserveOutputTrustLevel >= other.PreserveOutputTrustLevel;
        }

        /// <inheritdoc />
        public bool Equals(PreserveOutputsInfo other)
        {
            return Salt == other.Salt && PreserveOutputTrustLevel == other.PreserveOutputTrustLevel;
        }

        /// <inheritdoc />
        public override bool Equals(object other)
        {
            return StructUtilities.Equals(this, other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Salt.GetHashCode() ^ PreserveOutputTrustLevel.GetHashCode();
        }

        /// <nodoc />
        public static bool operator ==(PreserveOutputsInfo left, PreserveOutputsInfo right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PreserveOutputsInfo left, PreserveOutputsInfo right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Serialize with buildxl writer
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Salt.Serialize(writer);
            writer.WriteCompact(PreserveOutputTrustLevel);
        }

        /// <summary>
        /// Computes fingerprint.
        /// </summary>
        public void ComputeFingerprint(IFingerprinter fingerprinter)
        {
            fingerprinter.Add(nameof(Salt), Salt.ToHex());
            fingerprinter.Add(nameof(PreserveOutputTrustLevel), PreserveOutputTrustLevel);
        }
    }
}
