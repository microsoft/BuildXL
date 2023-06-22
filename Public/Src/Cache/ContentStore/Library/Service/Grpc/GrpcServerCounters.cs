// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc;

/// <nodoc />
public enum GrpcServerCounters
{
    /// <nodoc />
    [CounterType(CounterType.Stopwatch)]
    StreamContentReadFromDiskDuration,

    /// <nodoc />
    [CounterType(CounterType.Stopwatch)]
    CopyStreamContentDuration,

    /// <nodoc />
    CopyRequestRejected,

    /// <nodoc />
    CopyContentNotFound,

    /// <nodoc />
    CopyError,

    /// <nodoc />
    [CounterType(CounterType.Stopwatch)]
    HandlePushFile,

    /// <nodoc />
    [CounterType(CounterType.Stopwatch)]
    HandleCopyFile,

    /// <nodoc />
    PushFileRejectNotSupported,

    /// <nodoc />
    PushFileRejectCopyLimitReached,

    /// <nodoc />
    PushFileRejectCopyOngoingCopy,
}
