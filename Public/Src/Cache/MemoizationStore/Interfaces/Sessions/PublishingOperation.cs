// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.Serialization;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <nodoc />
    [DataContract]
    public struct PublishingOperation : IEquatable<PublishingOperation>
    {
        /// <nodoc />
        public StrongFingerprint StrongFingerprint { get; set; }

        /// <nodoc />
        public ContentHashListWithDeterminism ContentHashListWithDeterminism { get; set; }

        [OnDeserialized]
#pragma warning disable IDE0060 // Remove unused parameter
        private void Deserialize(StreamingContext context)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            {
                using var stream = new MemoryStream(_strongFingerprint);
                using var reader = new BuildXLReader(debug: false, stream, leaveOpen: false);
                StrongFingerprint = StrongFingerprint.Deserialize(reader);
            }

            {
                using var stream = new MemoryStream(_contentHashListWithDeterminism);
                using var reader = new BuildXLReader(debug: false, stream, leaveOpen: false);
                ContentHashListWithDeterminism = ContentHashListWithDeterminism.Deserialize(reader);
            }
        }

        [OnSerializing]
#pragma warning disable IDE0060 // Remove unused parameter
        private void Serialize(StreamingContext context)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            {
                using var stream = new MemoryStream();
                using var writer = new BuildXLWriter(debug: false, stream, leaveOpen: false, logStats: false);
                StrongFingerprint.Serialize(writer);
                _strongFingerprint = stream.ToArray();
            }

            {
                using var stream = new MemoryStream();
                using var writer = new BuildXLWriter(debug: false, stream, leaveOpen: false, logStats: false);
                ContentHashListWithDeterminism.Serialize(writer);
                _contentHashListWithDeterminism = stream.ToArray();
            }
        }

        [DataMember]
        private byte[] _strongFingerprint;

        [DataMember]
        private byte[] _contentHashListWithDeterminism;

        /// <inheritdoc />
        public override bool Equals(object obj) => obj is PublishingOperation op && Equals(op);

        /// <inheritdoc />
        public override int GetHashCode() => HashCodeHelper.Combine(StrongFingerprint.GetHashCode(), ContentHashListWithDeterminism.GetHashCode());

        /// <inheritdoc />
        public bool Equals(PublishingOperation other)
            => StrongFingerprint.Equals(other.StrongFingerprint) &&
                ContentHashListWithDeterminism.Equals(other.ContentHashListWithDeterminism);
    }
}
