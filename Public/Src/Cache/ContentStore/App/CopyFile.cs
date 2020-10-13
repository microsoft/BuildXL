// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using CLAP;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the CopyFile verb.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Copy file from another CASaaS")]
        internal void CopyFile(
            [Required, Description("Machine to copy from")] string host,
            [Required, Description("Expected content hash")] string hashString,
            [Required, Description("Path to destination file")] string destinationPath,
            [Description("Whether or not GZip is enabled"), DefaultValue(false)] bool useCompressionForCopies,
            [Description("File name where the GRPC port can be found when using cache service. 'CASaaS GRPC port' if not specified")] string grpcPortFileName,
            [Description("The GRPC port"), DefaultValue(0)] int grpcPort)
        {
            Initialize();

            var context = new Context(_logger);
            var retryPolicy = new RetryPolicy(
                new TransientErrorDetectionStrategy(),
                new FixedInterval("RetryInterval", (int)_retryCount, TimeSpan.FromSeconds(_retryIntervalSeconds), false));

            if (grpcPort == 0)
            {
                grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
            }

            if (!ContentHash.TryParse(hashString, out var hash))
            {
                throw new CacheException($"Invalid content hash string provided: {hashString}");
            }

            try
            {
                var config = GrpcCopyClientConfiguration.WithGzipCompression(useCompressionForCopies);
                config.BandwidthCheckerConfiguration = BandwidthChecker.Configuration.Disabled;
                using var clientCache = new GrpcCopyClientCache(context, new GrpcCopyClientCacheConfiguration()
                {
                    GrpcCopyClientConfiguration = config
                });

                var finalPath = new AbsolutePath(destinationPath);

                var copyFileResult = clientCache.UseAsync(new OperationContext(context), host, grpcPort, (nestedContext, rpcClient) =>
                {
                    return retryPolicy.ExecuteAsync(
                        () => rpcClient.CopyFileAsync(nestedContext, hash, finalPath, new CopyOptions(bandwidthConfiguration: null)));
                }).GetAwaiter().GetResult();

                if (!copyFileResult.Succeeded)
                {
                    throw new CacheException(copyFileResult.ErrorMessage);
                }
                else
                {
                    _logger.Debug($"Copy of {hashString} to {finalPath} was successful");
                }
            }
            catch (Exception ex)
            {
                throw new CacheException(ex.ToString());
            }
        }
    }
}
