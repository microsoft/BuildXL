// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Utils
{
    public static class HashSerializationExtensions
    {
        /// <nodoc />
        public static void Write(this ref SpanWriter writer, in ShortHash value)
        {
            writer.Write<ShortHash>(value);
        }

        /// <nodoc />
        public static ShortHash ReadShortHash(this SpanReader reader)
        {
            var span = reader.ReadSpan(ShortHash.SerializedLength, allowIncomplete: true);

            return ShortHash.FromSpan(span);
        }

        /// <nodoc />
        public static void Write(this ref SpanWriter writer, in ContentHash value)
        {
            writer.EnsureLength(ContentHash.SerializedLength);
            var writtenBytes = value.Serialize(writer.Remaining);
            writer.Advance(writtenBytes);
        }
    }
}
