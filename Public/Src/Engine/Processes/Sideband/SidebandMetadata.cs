// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Metadata associated with a sideband file.
    /// </summary>
    public sealed class SidebandMetadata : IEquatable<SidebandMetadata>
    {
        /// <summary>
        /// The current version of the sideband format.
        /// </summary>
        public const int FormatVersion = 2;

        /// <nodoc />
        public int Version { get; }

        /// <nodoc />
        public long PipSemiStableHash { get; }

        /// <nodoc />
        public byte[] StaticPipFingerprint { get; }

        /// <nodoc />
        public SidebandMetadata(long pipSemiStableHash, byte[] staticPipFingerprint)
            : this(FormatVersion, pipSemiStableHash, staticPipFingerprint)
        { }

        private SidebandMetadata(int version, long pipSemiStableHash, byte[] staticPipFingerprint)
        {
            Contract.Requires(staticPipFingerprint != null);

            Version = version;
            PipSemiStableHash = pipSemiStableHash;
            StaticPipFingerprint = staticPipFingerprint;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(Version);
            writer.Write(PipSemiStableHash);
            writer.WriteCompact(StaticPipFingerprint.Length);
            writer.Write(StaticPipFingerprint);
        }

        internal static SidebandMetadata Deserialize(BuildXLReader reader)
        {
            return new SidebandMetadata(
                reader.ReadInt32Compact(),
                reader.ReadInt64(),
                reader.ReadBytes(reader.ReadInt32Compact()));
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{{ Version: {Version}, PipId: {PipSemiStableHash:X16}, Fingerprint: {BitConverter.ToString(StaticPipFingerprint)} }}";
        }

        /// <inheritdoc />
        public bool Equals(SidebandMetadata other)
        {
            return
                other != null &&
                other.Version == Version &&
                other.PipSemiStableHash == PipSemiStableHash &&
                Enumerable.SequenceEqual(other.StaticPipFingerprint, StaticPipFingerprint);
        }
    }
}
