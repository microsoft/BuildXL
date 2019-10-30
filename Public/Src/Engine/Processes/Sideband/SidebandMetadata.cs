// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Metadata associated with a sideband file.
    /// </summary>
    public sealed class SidebandMetadata
    {
        private const int GlobalVersion = 1;

        /// <nodoc />
        public int Version { get; }

        /// <nodoc />
        public uint PipId { get; }

        /// <nodoc />
        public byte[] StaticPipFingerprint { get; }

        /// <nodoc />
        public SidebandMetadata(uint pipId, byte[] staticPipFingerprint)
            : this(GlobalVersion, pipId, staticPipFingerprint)
        { }

        private SidebandMetadata(int version, uint pipId, byte[] staticPipFingerprint)
        {
            Contract.Requires(staticPipFingerprint != null);

            Version = version;
            PipId = pipId;
            StaticPipFingerprint = staticPipFingerprint;
        }

        internal void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(Version);
            writer.Write(PipId);
            writer.WriteCompact(StaticPipFingerprint.Length);
            writer.Write(StaticPipFingerprint);
        }

        internal static SidebandMetadata Deserialize(BuildXLReader reader)
        {
            return new SidebandMetadata(
                reader.ReadInt32Compact(),
                reader.ReadUInt32(),
                reader.ReadBytes(reader.ReadInt32Compact()));
        }
    }
}
