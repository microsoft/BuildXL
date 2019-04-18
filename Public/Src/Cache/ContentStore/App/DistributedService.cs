// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.Host.Service;
using CLAP;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.ContentStore.App
{
    internal sealed partial class Application
    {
        /// <summary>
        /// Run the distributed service verb.
        /// </summary>
        [SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode")]
        [Verb(Description = "Run distributed CAS service")]
        internal void DistributedService
            (
            [Description("Cache name")] string cacheName,
            [Description("Cache root path")] string cachePath,
            [DefaultValue(ServiceConfiguration.GrpcDisabledPort), Description(GrpcPortDescription)] int grpcPort,
            [Description("Name of the memory mapped file used to share GRPC port. 'CASaaS GRPC port' if not specified.")] string grpcPortFileName,
            [DefaultValue(null), Description("Writable directory for service operations (use CWD if null)")] string dataRootPath,
            [DefaultValue(null), Description("Identifier for the stamp this service will run as")] string stampId,
            [DefaultValue(null), Description("Identifier for the ring this service will run as")] string ringId,
            [DefaultValue(Constants.OneMB), Description("Max size quota in MB")] int maxSizeQuotaMB,
            [DefaultValue(false)] bool useDistributedGrpc,
            [DefaultValue(false)] bool useCompressionForCopies
            )
        {
            Initialize();

            try
            {
                var cancellationTokenSource = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, args) =>
                {
                    cancellationTokenSource.Cancel();
                };

                var host = new HostInfo(stampId, ringId, new List<string>());

                if (grpcPort == 0)
                {
                    grpcPort = Helpers.GetGrpcPortFromFile(_logger, grpcPortFileName);
                }

                var arguments = CreateDistributedCacheServiceArguments(
                    copier: useDistributedGrpc ? new GrpcFileCopier(new Interfaces.Tracing.Context(_logger), grpcPort, useCompressionForCopies) : (IAbsolutePathFileCopier)new DistributedCopier(),
                    pathTransformer: useDistributedGrpc ? new GrpcDistributedPathTransformer() : (IAbsolutePathTransformer)new DistributedPathTransformer(),
                    host: host,
                    cacheName: cacheName,
                    cacheRootPath: cachePath,
                    grpcPort: (uint)grpcPort,
                    maxSizeQuotaMB: maxSizeQuotaMB,
                    dataRootPath: dataRootPath,
                    ct: cancellationTokenSource.Token);

                DistributedCacheServiceFacade.RunAsync(arguments).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }
        }

        private class TestHost : IDistributedCacheServiceHost
        {
            public string GetSecretStoreValue(string key)
            {
                return key;
            }

            public void OnStartedService()
            {
            }

            public Task OnStartingServiceAsync()
            {
                return Task.Run(() => { });
            }

            public void OnTeardownCompleted()
            {
            }

            public Task<Dictionary<string, string>> RetrieveKeyVaultSecretsAsync(List<string> secrets, CancellationToken token)
            {
                return Task.FromResult(new Dictionary<string, string>());
            }
        }
    }
}
