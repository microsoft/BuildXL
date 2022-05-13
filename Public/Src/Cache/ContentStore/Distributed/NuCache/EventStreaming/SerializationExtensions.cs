// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal static class SerializationExtensions
    {
        public static void Write(this BuildXLWriter writer, in ShortHash value)
        {
            value.Serialize(writer);
        }

        public static ShortHash ReadShortHash(this BuildXLReader reader)
        {
            // TODO: use pooled buffers in .net core,
            // or even add an option to convert ShortHash to Span<byte> and use reader.Read(spanOfBytes);
            // Work item: 1943555
            var data = reader.ReadBytes(ShortHash.SerializedLength);
            return ShortHash.FromBytes(data);
        }

        public static void Write(this BuildXLWriter writer, UnixTime value)
        {
            writer.WriteCompact(value.Value);
        }

        public static UnixTime ReadUnixTime(this BuildXLReader reader)
        {
            return new UnixTime(reader.ReadInt64Compact());
        }
    }
}
