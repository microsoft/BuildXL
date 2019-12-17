using System;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;
using StructUtilities = BuildXL.Cache.ContentStore.Interfaces.Utils.StructUtilities;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    ///  Preserve outputs Salt contains content hash and preserveoutputTrustLevel.
    /// </summary>
    public struct PreserveOutputsSalt : IEquatable<PreserveOutputsSalt>
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
        /// Initializes a new instance of the <see cref="PreserveOutputsSalt" /> struct from content hash and trust level
        /// </summary>
        public PreserveOutputsSalt(ContentHash preserveOutputSalt, int preserveOutputTrustLevel)
        {
            Salt = preserveOutputSalt;
            PreserveOutputTrustLevel = preserveOutputTrustLevel;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="PreserveOutputsSalt" /> struct from buildxl reader
        /// </summary>
        public PreserveOutputsSalt(BuildXLReader reader)
        {
            Salt = new ContentHash(reader);
            PreserveOutputTrustLevel = reader.ReadInt32Compact();
        }

        /// <summary>
        /// Check if is as safe or safer than 
        /// </summary>
        public bool IsAsSafeOrSaferThan(PreserveOutputsSalt other)
        {
            return Salt == other.Salt && PreserveOutputTrustLevel >= other.PreserveOutputTrustLevel;
        }

        /// <inheritdoc />
        public bool Equals(PreserveOutputsSalt other)
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
        public static bool operator ==(PreserveOutputsSalt left, PreserveOutputsSalt right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PreserveOutputsSalt left, PreserveOutputsSalt right)
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

    }
}
