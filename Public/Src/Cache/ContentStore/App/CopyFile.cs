// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using CLAP;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.Practices.TransientFaultHandling;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

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

            if (!ContentHash.TryParse(hashString, out ContentHash hash))
            {
                throw new CacheException($"Invalid content hash string provided: {hashString}");
            }

            try
            {
                using (var rpcClient = new GrpcCopyClient(host, grpcPort))
                {
                    var finalPath = new AbsolutePath(destinationPath);

                    // This action is synchronous to make sure the calling application doesn't exit before the method returns.
                    var copyFileResult = retryPolicy.ExecuteAsync(() => rpcClient.CopyFileAsync(context, hash, finalPath)).Result;
                    if (!copyFileResult.Succeeded)
                    {
                        throw new CacheException(copyFileResult.ErrorMessage);
                    }
                    else
                    {
                        _logger.Debug($"Copy of {hashString} to {finalPath} was successful");
                    }

                    var shutdownResult = rpcClient.ShutdownAsync(context).Result;
                    if (!shutdownResult.Succeeded)
                    {
                        throw new CacheException(shutdownResult.ErrorMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CacheException(ex.ToString());
            }
        }
    }
}
