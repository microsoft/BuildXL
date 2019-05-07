// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Native.IO;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Distributed.Stores
{
    public class TestDistributedCacheSameMachine : TestBase
    {
        private const int ReadyWaitMs = 5000;
        private const int ShutdownWaitMs = 1000;

        public TestDistributedCacheSameMachine()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task TestPutThenPlace()
        { 
            if (string.IsNullOrEmpty(GetConnectionString()))
            {
                // Only run this test if the connection string is defined
                Console.WriteLine("Couldn't find connection string!");
                return;
            }

            Console.WriteLine("Found connection string!");

            var context = new Context(TestGlobal.Logger);
            using (var d = new DisposableDirectory(FileSystem))
            {
                string ringId = GetRandomFileName();
                string stampId = GetRandomFileName();

                // Define cache 1
                var distributedConfig1 = CreateRandomDistributedConfig(d, stampId, ringId);
                var putScenario = ThreadSafeRandom.Generator.Next().ToString();
                var cacheProcess1 = new ServiceProcess(distributedConfig1, null, putScenario, ReadyWaitMs, ShutdownWaitMs);

                // Define cache 2
                var distributedConfig2 = CreateRandomDistributedConfig(d, stampId, ringId);
                var placeScenario = ThreadSafeRandom.Generator.Next().ToString();
                var cacheProcess2 = new ServiceProcess(distributedConfig2, null, placeScenario, ReadyWaitMs, ShutdownWaitMs);

                using (cacheProcess1)
                using (cacheProcess2)
                {
                    try
                    {
                        // Startup cache 1
                        await cacheProcess1.StartupAsync(context).ShouldBeSuccess();
                        Logger.Debug(cacheProcess1.GetLogs());

                        try
                        {
                            // Startup cache 2
                            await cacheProcess2.StartupAsync(context).ShouldBeSuccess();
                            Logger.Debug(cacheProcess2.GetLogs());

                            // Generate random file
                            var bytes = ThreadSafeRandom.GetBytes(1024);
                            var filePath = d.Path / GetRandomFileName();
                            await FileUtilities.WriteAllBytesAsync(filePath.Path, bytes);
                            var contentHash = bytes.CalculateHash(HashType.Vso0);


                            // Ensure the GRPC servers have successfully spun up
                            var boolResults = await Task.WhenAll(CheckGrpcPortIsOpen(context, distributedConfig1.GrpcPort), CheckGrpcPortIsOpen(context, distributedConfig2.GrpcPort));
                            boolResults.Select(br => br.ShouldBeSuccess());

                            // Put random file into cache 1
                            var putConfiguration = new PutConfiguration(
                                contentHash.HashType,
                                filePath,
                                distributedConfig1.GrpcPort,
                                distributedConfig1.CacheName,
                                null);

                            var putProcess = new ServiceProcess(putConfiguration, null, putScenario, ReadyWaitMs, ShutdownWaitMs, true);

                            using (putProcess)
                            {
                                try
                                {
                                    await putProcess.StartupAsync(context).ShouldBeSuccess();
                                    Logger.Debug(putProcess.GetLogs());
                                    await Task.Delay(ReadyWaitMs);
                                }
                                finally
                                {
                                    await putProcess.ShutdownAsync(context).ShouldBeSuccess();
                                    Logger.Debug(putProcess.GetLogs());
                                }
                            }

                            // Place random file from cache 2
                            // Cache 2 should:
                            // * Fail to find the content for the given hash in the local cache
                            // * Ask redis if the content exists elsewhere, find that cache 1 has it
                            // * Copy the content from cache 1's local cache into cache 2's local cache
                            // * Place the content from cache 2's local cache to the requested path
                            var placeConfiguration = new PlaceConfiguration(
                                contentHash,
                                d.Path / GetRandomFileName(),
                                distributedConfig2.GrpcPort,
                                distributedConfig2.CacheName,
                                null);
                            var placeProcess = new ServiceProcess(placeConfiguration, null, placeScenario, ReadyWaitMs, ShutdownWaitMs, true);

                            using (placeProcess)
                            {
                                try
                                {
                                    await placeProcess.StartupAsync(context).ShouldBeSuccess();
                                    Logger.Debug(placeProcess.GetLogs());
                                }
                                finally
                                {
                                    await placeProcess.ShutdownAsync(context).ShouldBeSuccess();
                                    Logger.Debug(placeProcess.GetLogs());
                                }
                            }
                        }
                        finally
                        {
                            // Shutdown cache 2
                            if (cacheProcess2 != null)
                            {
                                var shutdownResult2 = await cacheProcess2.ShutdownAsync(context);
                                Logger.Debug(cacheProcess2.GetLogs());
                            }
                        }
                    }
                    finally
                    {
                        // Shutdown cache 1
                        if (cacheProcess1 != null)
                        {
                            var shutdownResult1 = await cacheProcess1.ShutdownAsync(context);
                            Logger.Debug(cacheProcess1.GetLogs());
                        }
                    }
                }
            }
        }

        private async Task<BoolResult> CheckGrpcPortIsOpen(Context context, uint grpcPort)
        {
            var client = new GrpcContentClient(new ServiceClientContentSessionTracer(nameof(CheckGrpcPortIsOpen)), FileSystem, (int)grpcPort, nameof(CheckGrpcPortIsOpen));

            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ReadyWaitMs * 5)
            {
                try
                {
                    var startupResult = await client.StartupAsync(context);
                    if (startupResult.Succeeded)
                    {
                        return BoolResult.Success;
                    }
                }
                catch (ClientCanRetryException)
                { }

                // Wait a short time so we don't churn
                await Task.Delay(100);
            }

            return new BoolResult($"Failed to detect active grpc client for {grpcPort}");
        }

        private string GetConnectionString()
        {
            return Environment.GetEnvironmentVariable(EnvironmentConnectionStringProvider.RedisConnectionStringEnvironmentVariable);
        }

        private DistributedServiceConfiguration CreateRandomDistributedConfig(DisposableDirectory d, string stampId, string ringId)
        {
            return new DistributedServiceConfiguration(
                    dataRootPath: d.Path / GetRandomFileName(),
                    gracefulShutdownSeconds: 5,
                    grpcPort: (uint)PortExtensions.GetNextAvailablePort(),
                    cacheName: ThreadSafeRandom.Generator.Next().ToString(),
                    cachePath: d.Path / GetRandomFileName(),
                    stampId: stampId,
                    ringId: ringId);
        }
    }
}
