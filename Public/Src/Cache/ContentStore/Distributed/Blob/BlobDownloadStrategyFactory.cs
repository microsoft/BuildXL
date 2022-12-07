// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    public enum BlobDownloadStrategy
    {
        BlobSdkDownloadToFile,
        BlobSdkDownloadToStream,
        BlobSdkDownloadToMemoryMappedFile,
        HttpClientDownloadToStream,
        HttpClientDownloadToMemoryMappedFile,
    }

    public record BlobDownloadStrategyConfiguration(
        BlobDownloadStrategy Strategy = BlobDownloadStrategy.HttpClientDownloadToStream,
        RetryPolicyConfiguration? RetryPolicyConfiguration = null,
        int FileDownloadBufferSize = 81920);

    public static class BlobDownloadStrategyFactory
    {
        public static IBlobDownloadStrategy Create(BlobDownloadStrategyConfiguration configuration, IClock clock)
        {
            return configuration.Strategy switch {
                BlobDownloadStrategy.BlobSdkDownloadToFile => new BlobSdkDownloadToFileStrategy(configuration),
                BlobDownloadStrategy.BlobSdkDownloadToStream => new BlobSdkDownloadToStreamStrategy(configuration),
                BlobDownloadStrategy.HttpClientDownloadToStream => new HttpClientDownloadToStreamStrategy(configuration, clock),
                BlobDownloadStrategy.HttpClientDownloadToMemoryMappedFile => new HttpClientDownloadToMemoryMappedFileStrategy(configuration, clock),
                _ => throw new NotImplementedException($"Unhandled blob download strategy `{configuration.Strategy}`"),
            };
        }
    }
}
