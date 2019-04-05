// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Engine.Distribution.Grpc
{
    internal static class GrpcConstants
    {
        public const string TraceIdKey = "traceid-bin";
        public const string BuildIdKey = "buildid";
        public const int MaxRetryForGrpcMessages = 3;
    }
}