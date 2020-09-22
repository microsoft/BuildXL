// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
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
        /// Run the CopyFileTo verb.
        /// </summary>
        [Verb(Description = "Copy file to another CASaaS")]
        internal void CopyFileTo(
            [Required, Description("Machine to copy to")] string host,
            [Required, Description("Path to source file")] string sourcePath,
            [Description("File name where the GRPC port can be found when using cache service. 'CASaaS GRPC port' if not specified")] string grpcPortFileName,
            [Description("The GRPC port"), DefaultValue(0)] int grpcPort)
        {
            Initialize();

            var context = new Context(_logger);
            var operationContext = new OperationContext(context, CancellationToken.None);
            var retryPolicy = new RetryPolicy(
                new TransientErrorDetectionStrategy(),
                new FixedInterval("RetryInterval", (int)_retryCount, TimeSpan.FromSeconds(_retryIntervalSeconds), false));

            if (grpcPort == 0)
            {
                grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
            }

            var hasher = ContentHashers.Get(HashType.MD5);
            var bytes = File.ReadAllBytes(sourcePath);
            var hash = hasher.GetContentHash(bytes);

            try
            {
                var path = new AbsolutePath(sourcePath);
                using Stream stream = File.OpenRead(path.Path);

                using var clientCache = new GrpcCopyClientCache(context, new GrpcCopyClientCacheConfiguration() {
                    GrpcCopyClientConfiguration = GrpcCopyClientConfiguration.WithGzipCompression(false),
                });
                var copyFileResult = clientCache.UseAsync(operationContext, host, grpcPort, (nestedContext, rpcClient) =>
                {
                    return retryPolicy.ExecuteAsync(() => rpcClient.PushFileAsync(nestedContext, hash, stream));
                }).GetAwaiter().GetResult();

                if (!copyFileResult.Succeeded)
                {
                    context.Error($"{copyFileResult}");
                    throw new CacheException(copyFileResult.ErrorMessage);
                }
                else
                {
                    context.Info($"Copy of {sourcePath} was successful");
                }
            }
            catch (Exception ex)
            {
                throw new CacheException(ex.ToString());
            }
        }
    }
}
