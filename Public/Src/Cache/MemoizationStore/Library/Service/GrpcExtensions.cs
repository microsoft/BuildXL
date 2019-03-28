// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using Google.Protobuf;

namespace BuildXL.Cache.MemoizationStore.Service
{
    internal static class GrpcExtensions
    {
        public static ByteString ToByteString(in this Fingerprint fingerprint)
        {
            byte[] hashByteArray = fingerprint.ToByteArray();
            return ByteString.CopyFrom(hashByteArray, 0, hashByteArray.Length);
        }

        public static ByteString ToByteString(this byte[] data)
        {
            if (data == null)
            {
                return ByteString.Empty;
            }

            return ByteString.CopyFrom(data, 0, data.Length);
        }

        public static ByteString ToByteString(this IReadOnlyCollection<byte> data)
        {
            if (data == null)
            {
                return ByteString.Empty;
            }

            // TODO: extra allocation!
            return ToByteString(data.ToArray());
        }
    }
}
