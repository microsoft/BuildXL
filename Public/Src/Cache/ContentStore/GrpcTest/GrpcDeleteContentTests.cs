﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Grpc
{
    public class GrpcDeleteContentTests : TestBase
    {
        private const string CacheName = "CacheName";
        private const string SessionName = "SessionName";

        public GrpcDeleteContentTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        [Theory]
        [InlineData(10L)]
        [InlineData(1000L)]
        public Task SendReceiveDeletion(long size)
        {
            var scenario = nameof(GrpcDeleteContentTests) + nameof(SendReceiveDeletion);
            return RunServerTestAsync(new Context(Logger), scenario, async (context, config, rpcClient) =>
            {
                // Create random file to put
                byte[] content = new byte[size];
                ThreadSafeRandom.Generator.NextBytes(content);

                AbsolutePath fileName = new AbsolutePath(Path.Combine(config.DataRootPath.Path, Guid.NewGuid().ToString()));
                File.WriteAllBytes(fileName.Path, content);

                // Put content
                var putResult = await rpcClient.PutFileAsync(context, HashType.Vso0, fileName, FileRealizationMode.Copy);
                putResult.ShouldBeSuccess();

                // Place content to prove it exists
                var placeResult = await rpcClient.PlaceFileAsync(context, putResult.ContentHash, new AbsolutePath(fileName.Path + "place"), FileAccessMode.None, FileReplacementMode.None, FileRealizationMode.Copy);
                placeResult.ShouldBeSuccess();

                // Delete content
                var deleteResult = await rpcClient.DeleteContentAsync(context, putResult.ContentHash);
                deleteResult.ShouldBeSuccess();
                deleteResult.ContentHash.Equals(putResult.ContentHash);
                string.IsNullOrEmpty(deleteResult.ErrorMessage).Should().BeTrue();
                deleteResult.EvictedSize.Should().Be(size);
                deleteResult.PinnedSize.Should().Be(0L);

                // Fail to place content
                var failPlaceResult = await rpcClient.PlaceFileAsync(context, putResult.ContentHash, new AbsolutePath(fileName.Path + "fail"), FileAccessMode.None, FileReplacementMode.None, FileRealizationMode.Copy);
                failPlaceResult.Succeeded.Should().BeFalse();
                failPlaceResult.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedContentNotFound);
            });
        }

        private async Task RunServerTestAsync(Context context, string scenario, Func<Context, ServiceConfiguration, IRpcClient, Task> funcAsync)
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var storeConfig = new ContentStoreConfiguration(new MaxSizeQuota($"{1 * 1024 * 1024}"));
                await storeConfig.Write(FileSystem, directory.Path).ConfigureAwait(false);

                var serviceConfig =
                    new ServiceConfiguration(
                        new Dictionary<string, AbsolutePath> { { CacheName, directory.Path } },
                        directory.Path,
                        ServiceConfiguration.DefaultMaxConnections,
                        ServiceConfiguration.DefaultGracefulShutdownSeconds,
                        PortExtensions.GetNextAvailablePort(),
                        Guid.NewGuid().ToString());

                //using (var server = new ServiceProcess(serviceConfig, null, scenario, 10000, 30000))
                using (var server = new LocalContentServer(FileSystem, Logger, scenario, path => new FileSystemContentStore(FileSystem, SystemClock.Instance, path), new LocalServerConfiguration(serviceConfig)))
                {
                    BoolResult r = await server.StartupAsync(context).ConfigureAwait(false);
                    r.ShouldBeSuccess();

                using (var rpcClient = new GrpcContentClient(new ServiceClientContentSessionTracer(scenario), FileSystem, (int)serviceConfig.GrpcPort, scenario))
                {
                    try
                    {
                        var createSessionResult = await rpcClient.CreateSessionAsync(context, SessionName, CacheName, ImplicitPin.None);
                        createSessionResult.ShouldBeSuccess();

                        await funcAsync(context, serviceConfig, rpcClient);
                    }
                    finally
                    {
                        (await rpcClient.ShutdownAsync(context)).ShouldBeSuccess();
                    }
                }

                    r = await server.ShutdownAsync(context);
                    r.ShouldBeSuccess();
                }
            }
        }
    }
}
