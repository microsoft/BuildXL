// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Grpc
{
    public static class CopyConstants
    {
        public const int DefaultBufferSize = 8192;

        public static TimeSpan DefaultConnectionTimeoutSeconds = Timeout.InfiniteTimeSpan;
    }
}
