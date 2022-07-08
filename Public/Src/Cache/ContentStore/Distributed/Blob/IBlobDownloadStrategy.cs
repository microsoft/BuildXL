// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using Microsoft.WindowsAzure.Storage.Blob;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    public interface IBlobDownloadStrategy
    {
        Task<RemoteDownloadResult> DownloadAsync(
            OperationContext context,
            RemoteDownloadRequest downloadRequest);

        Task<RemoteDownloadResult> RemoteDownloadAsync(
            OperationContext context,
            RemoteDownloadRequest downloadRequest);
    }

    public struct RemoteDownloadRequest
    {
        public ContentHash ContentHash { get; init; }

        public AbsolutePath AbsolutePath { get; init; }

        public CloudBlockBlob Reference { get; init; }

        public string DownloadUrl { get; init; }
    }

    public struct RemoteDownloadResult
    {
        public PlaceFileResult.ResultCode ResultCode { get; init; }

        public long? FileSize { get; init; }

        public TimeSpan? TimeToFirstByteDuration { get; init; }

        public DownloadResult? DownloadResult { get; init; }

        public override string ToString()
        {
            return $"{nameof(ResultCode)}=[{ResultCode}] " +
                $"{nameof(FileSize)} =[{FileSize ?? -1}] " +
                $"{nameof(TimeToFirstByteDuration)}=[{TimeToFirstByteDuration ?? TimeSpan.Zero}] " +
                $"{DownloadResult.ToString() ?? ""}";
        }
    }

    public struct DownloadResult
    {
        public long DegreeOfParallelism { get; init; }

        public TimeSpan OpenFileStreamDuration { get; init; }

        public TimeSpan? MemoryMapDuration { get; init; }

        public TimeSpan? DownloadDuration { get; init; }

        public TimeSpan? WriteDuration { get; init; }

        public override string ToString()
        {
            return $"DegreeOfParallelism=[{DegreeOfParallelism}] " +
                $"OpenFileStreamDurationMs=[{OpenFileStreamDuration.TotalMilliseconds}] " +
                $"MemoryMapDurationMs=[{MemoryMapDuration?.TotalMilliseconds ?? -1}] " +
                $"DownloadDurationMs=[{DownloadDuration?.TotalMilliseconds ?? -1}]" +
                $"WriteDuration=[{WriteDuration?.TotalMilliseconds ?? -1}]";
        }
    }
}
