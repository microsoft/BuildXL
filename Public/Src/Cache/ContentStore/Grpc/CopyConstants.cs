// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.ContentStore.Grpc
{
    public static class CopyConstants
    {
        public static readonly int DefaultBufferSize = 8192;

        public static readonly int CompressionSize = DefaultBufferSize * 8;
    }
}
