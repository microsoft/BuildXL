// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal static class SerializationExtensions
    {
        public static void Write(this BuildXLWriter writer, in ShortHash value)
        {
            value.Value.Serialize(writer, ShortHash.SerializedLength);
        }

        public static ShortHash ReadShortHash(this BuildXLReader reader)
        {
            return new ShortHash(ReadOnlyFixedBytes.ReadFrom(reader, ShortHash.SerializedLength));
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
