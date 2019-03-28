// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Cache.VerticalAggregator
{
    internal static class FailureConstants
    {
        public const string FailureRemoteGet = "Failure_RemoteGet";
        public const string FailureCASUpload = "Failure_CASUpload";
        public const string FailureCASDownload = "Failure_CASDownload";
        public const string FailureRemoteAdd = "Failure_RemoteAdd";
        public const string FailureLocalAdd = "Failure_LocalAdd";
        public const string FailureLocalAndRemote = "Failure_LocalAndRemote";
        public const string FailureLocal = "Failure_Local";
        public const string FailureRemote = "Failure_Remote";
        public const string FailureSourcePin = "Failure_SourcePin";
        public const string FailureGetSourceStream = "Failure_GetSourceStream";
        public const string FailureRemotePin = "Failure_RemotePin";
    }
}
