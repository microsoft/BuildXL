// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
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
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Stores
{
    public class GrpcCopyContentTests : TestBase
    {
        private const int FileSize = 1000;
        private const HashType DefaultHashType = HashType.Vso0;
        private const string LocalHost = "localhost";
        private readonly Context _context;
        private GrpcCopyClientCache _clientCache;

        private int? _copyToLimit = null;
        private int? _proactivePushCountLimit = null;

        public GrpcCopyContentTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
            _context = new Context(Logger);
            _clientCache = new GrpcCopyClientCache(_context, new GrpcCopyClientCacheConfiguration()
            {
                ResourcePoolConfiguration = new BuildXL.Cache.ContentStore.Utils.ResourcePoolConfiguration()
                {
                    MaximumResourceCount = 1024,
                },
                GrpcCopyClientConfiguration = new GrpcCopyClientConfiguration()
                {
                    PropagateCallingMachineName = true,
                }
            });
        }

        [Fact]
        public async Task CopyFileAndConnectAtStartup()
        {
            await Task.Yield();

            _clientCache = new GrpcCopyClientCache(_context, new GrpcCopyClientCacheConfiguration()
            {
                ResourcePoolConfiguration = new BuildXL.Cache.ContentStore.Utils.ResourcePoolConfiguration()
                {
                    MaximumResourceCount = 1024,
                    
                },
                GrpcCopyClientConfiguration = new GrpcCopyClientConfiguration()
                {
                    PropagateCallingMachineName = true,
                    
                    ConnectOnStartup = true,
                },
                ResourcePoolVersion = GrpcCopyClientCacheConfiguration.PoolVersion.V2,
            });

            await CopyExistingFile();
        }


        [Fact(Skip = "Flaky")]
        public Task CopyFailWithUnknownError()
        {
            return RunTestCase(async (server, rootPath, session, client) =>
                               {
                                   // Write a random file
                                   var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                                   var content = ThreadSafeRandom.GetBytes(FileSize);
                                   FileSystem.WriteAllBytes(sourcePath, content);

                                   // Put the random file
                                   PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                                   putResult.ShouldBeSuccess();

                                   // Copy the file out via GRPC
                                   var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();

                                   // Injecting failure.
                                   server.GrpcContentServer.HandleRequestFailure = new Exception("Custom exception");
                                   var copyFileResult = await client.CopyFileAsync(new OperationContext(_context), putResult.ContentHash, destinationPath, new CopyOptions(bandwidthConfiguration: null));
                                   copyFileResult.ShouldBeError("Custom exception");
                               });
        }

        [Fact]
        public Task PushFileFailsWithUnknownError()
        {
            return RunTestCase(async (server, rootPath, session, client) =>
                               {
                                   var data = ThreadSafeRandom.GetBytes(1 + 42);
                                   using var stream = new MemoryStream(data);
                                   var hash = HashInfoLookup.GetContentHasher(HashType.Vso0).GetContentHash(data);

                                   // Injecting failure.
                                   server.GrpcContentServer.HandleRequestFailure = new Exception("Custom exception");

                                   var pushResult = await client.PushFileAsync(
                                       new OperationContext(_context),
                                       hash,
                                       stream,
                                       new CopyOptions(bandwidthConfiguration: null));
                                   pushResult.ShouldBeError("Custom exception");
                               });
        }

        [Fact]
        public Task CopyExistingFile()
        {
            return RunTestCase(async (rootPath, session, client) =>
             {
                // Write a random file
                var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                 var content = ThreadSafeRandom.GetBytes(FileSize);
                 FileSystem.WriteAllBytes(sourcePath, content);

                // Put the random file
                PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                 putResult.ShouldBeSuccess();

                // Copy the file out via GRPC
                var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                 (await client.CopyFileAsync(new OperationContext(_context), putResult.ContentHash, destinationPath, new CopyOptions(bandwidthConfiguration: null))).ShouldBeSuccess();

                 var copied = FileSystem.ReadAllBytes(destinationPath);

                // Compare original and copied files
                var originalHash = content.CalculateHash(DefaultHashType);
                 var copiedHash = copied.CalculateHash(DefaultHashType);
                 Assert.Equal(originalHash, copiedHash);
             });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task CopyFileRejectedIfTooMany(bool failFastIfServerBusy)
        {
            var failFastBandwidthConfiguration = new BandwidthConfiguration()
            {
                Interval = TimeSpan.FromSeconds(10),
                RequiredBytes = 10_000_000,
                FailFastIfServerIsBusy = failFastIfServerBusy,
            };

            int numberOfFiles = 100;
            _copyToLimit = 1;

            return RunTestCase(async (rootPath, session, client) =>
             {
                // Add random files to the cache.
                var tasks = Enumerable.Range(1, numberOfFiles).Select(_ => putRandomFile()).ToList();
                 var hashes = await Task.WhenAll(tasks);

                 var copyTasks = hashes.Select(
                     hash =>
                     {
                         var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                         return client.CopyFileAsync(
                             new OperationContext(_context),
                             hash,
                             destinationPath,
                             new CopyOptions(failFastBandwidthConfiguration));
                     });

                 var results = await Task.WhenAll(copyTasks);

                 if (failFastIfServerBusy)
                 {
                    // We're doing 100 simultaneous copies, at least some of them should fail, because we're not willing to wait for the response.
                    var error = results.FirstOrDefault(r => !r.Succeeded);
                     error.Should().NotBeNull("At least one copy operation should fail.");

                     error!.ErrorMessage.Should().Contain("Copy limit of");
                 }
                 else
                 {
                    // All operation should succeed!
                    results.All(r => r.ShouldBeSuccess()).Should().BeTrue();
                 }

                 async Task<ContentHash> putRandomFile()
                 {
                     var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                     var content = ThreadSafeRandom.GetBytes(FileSize);
                     FileSystem.WriteAllBytes(sourcePath, content);

                    // Put the random file
                    PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                     putResult.ShouldBeSuccess();
                     return putResult.ContentHash;
                 }
             });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task PushFileAsync(bool limitProactiveCopies)
        {
            await Task.Yield();

            _proactivePushCountLimit = limitProactiveCopies ? 1 : 10000;
            int numberOfPushes = 100;
            await RunTestCase(async (rootPath, session, client) =>
                              {
                                  var input = Enumerable.Range(1, numberOfPushes)
                                      .Select(r => ThreadSafeRandom.GetBytes(1 + 42))
                                      .Select(data => (stream: new MemoryStream(data), hash: HashInfoLookup.GetContentHasher(HashType.Vso0).GetContentHash(data)))
                                      .ToList();

                                  var pushTasks = input.Select(
                                      tpl =>
                                          client.PushFileAsync(
                                              new OperationContext(_context),
                                              tpl.hash,
                                              tpl.stream,
                                              new CopyOptions(bandwidthConfiguration: null))).ToList();

                                  var results = await Task.WhenAll(pushTasks);

                                  if (limitProactiveCopies)
                                  {
                                      // We're doing 100 simultaneous copies, at least some of them should fail, because we're not willing to wait for the response.
                                      var error = results.FirstOrDefault(r => !r.Succeeded);
                                      error.Should().NotBeNull("At least one copy operation should fail.");

                                      error!.ErrorMessage.Should().Contain("CopyLimitReached");
                                  }
                                  else
                                  {
                                      // All operation should succeed!
                                      results.All(r => r.ShouldBeSuccess().Succeeded).Should().BeTrue();
                                  }
                              });
        }

        [Fact]
        public async Task PushIsRejectedForTheSameHash()
        {
            await Task.Yield();

            int numberOfPushes = 100;
            await RunTestCase(async (rootPath, session, client) =>
                              {
                                  var bytes = ThreadSafeRandom.GetBytes(1 + 42);
                                  var input = Enumerable.Range(1, numberOfPushes)
                                      .Select(data => (stream: new MemoryStream(bytes), hash: HashInfoLookup.GetContentHasher(HashType.Vso0).GetContentHash(bytes)))
                                      .ToList();

                                  var pushTasks = input.Select(
                                      tpl =>
                                          client.PushFileAsync(
                                              new OperationContext(_context),
                                              tpl.hash,
                                              tpl.stream,
                                              new CopyOptions(bandwidthConfiguration: null))).ToList();

                                  var results = await Task.WhenAll(pushTasks);

                                  results.Any(r => r.Status == CopyResultCode.Rejected_OngoingCopy).Should().BeTrue();

                                  var result = await client.PushFileAsync(
                                      new OperationContext(_context),
                                      input[0].hash,
                                      input[0].stream,
                                      new CopyOptions(bandwidthConfiguration: null));
                                  result.Status.Should().Be(CopyResultCode.Rejected_ContentAvailableLocally);
                              });
        }

        [Fact]
        public async Task CopyFileShouldPropagateMachineName()
        {
            await CopyExistingFile();

            var copyFileStopLines = GetOutputLines().Where(l => l.Contains("GrpcContentServer.CopyFileAsync stop")).ToList();
            copyFileStopLines.FirstOrDefault(l => l.Contains("Sender=[localhost")).Should().BeNull($"'localhost' should not be present in the output lines: {string.Join(", ", copyFileStopLines)}");
            copyFileStopLines.All(l => l.Contains("Sender=[")).Should().BeTrue();
        }

        [Fact]
        public Task CopyToShouldNotCloseGivenStream()
        {
            return RunTestCase(async (rootPath, session, client) =>
             {
                // Write a random file
                var sourcePath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                 var content = ThreadSafeRandom.GetBytes(FileSize);
                 FileSystem.WriteAllBytes(sourcePath, content);

                // Put the random file
                PutResult putResult = await session.PutFileAsync(_context, HashType.Vso0, sourcePath, FileRealizationMode.Any, CancellationToken.None);
                 putResult.ShouldBeSuccess();

                // Copy the file out via GRPC
                var destinationPath = rootPath / ThreadSafeRandom.Generator.Next().ToString();
                 using (var destinationStream = FileSystem.Open(
                     destinationPath,
                     FileAccess.ReadWrite,
                     FileMode.CreateNew,
                     FileShare.None,
                     FileOptions.None,
                     1024))
                 {
                     (await client.CopyToAsync(new OperationContext(_context), putResult.ContentHash, destinationStream, new CopyOptions(bandwidthConfiguration: null))).ShouldBeSuccess();
                    // If the stream is not disposed, the following operation should not fail.
                    destinationStream.Stream.Position.Should().BeGreaterThan(0);
                 }

                 var copied = FileSystem.ReadAllBytes(destinationPath);

                // Compare original and copied files
                var originalHash = content.CalculateHash(DefaultHashType);
                 var copiedHash = copied.CalculateHash(DefaultHashType);
                 Assert.Equal(originalHash, copiedHash);
             });
        }

        [Fact]
        public Task CopyNonExistingFile()
        {
            return RunTestCase(async (rootPath, session, client) =>
             {
                // Copy the file out via GRPC
                var copyFileResult = await client.CopyFileAsync(new OperationContext(_context), ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), new CopyOptions(bandwidthConfiguration: null));

                 Assert.False(copyFileResult.Succeeded);
                 copyFileResult.Code.Should().Be(CopyResultCode.FileNotFoundError, copyFileResult.ToString());
             });
        }
        
        [Fact]
        public Task WrongPort()
        {
            return RunTestCase(async (rootPath, session, client) =>
             {
                // Copy fake file out via GRPC
                var bogusPort = PortExtensions.GetNextAvailablePort();

                 await _clientCache.UseAsync(new OperationContext(_context), LocalHost, bogusPort, async (nestedContext, client) =>
                 {
                     var copyFileResult = await client.CopyFileAsync(nestedContext, ContentHash.Random(), rootPath / ThreadSafeRandom.Generator.Next().ToString(), new CopyOptions(bandwidthConfiguration: null));
                     Assert.Equal(CopyResultCode.ServerUnavailable, copyFileResult.Code);
                     return Unit.Void;
                 });
             });
        }

        private Task RunTestCase(Func<AbsolutePath, IContentSession, GrpcCopyClient, Task> testAct, [CallerMemberName] string testName = null)
        {
            return RunTestCase((_, absolutePath, contentSession, grpcCopyClient) => testAct(absolutePath, contentSession, grpcCopyClient), testName);
        }

        private async Task RunTestCase(Func<LocalContentServer, AbsolutePath, IContentSession, GrpcCopyClient, Task> testAct, [CallerMemberName] string testName = null)
        {
            var cacheName = testName + "_cache";
            testName += Guid.NewGuid(); // Using a guid to disambiguate scenario name.

            using (var directory = new DisposableDirectory(FileSystem))
            {
                var rootPath = directory.Path;

                var namedCacheRoots = new Dictionary<string, AbsolutePath> { { cacheName, rootPath } };
                var grpcPort = PortExtensions.GetNextAvailablePort();
                var grpcPortFileName = Guid.NewGuid().ToString();
                var configuration = new ServiceConfiguration(
                                        namedCacheRoots,
                                        rootPath,
                                        ServiceConfiguration.DefaultGracefulShutdownSeconds,
                                        grpcPort,
                                        grpcPortFileName) {CopyRequestHandlingCountLimit = _copyToLimit, ProactivePushCountLimit = _proactivePushCountLimit};

                var storeConfig = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
                Func<AbsolutePath, IContentStore> contentStoreFactory = (path) =>
                    new FileSystemContentStore(
                        FileSystem,
                        SystemClock.Instance,
                        directory.Path,
                        new ConfigurationModel(storeConfig));

                var server = new LocalContentServer(FileSystem, Logger, testName, contentStoreFactory, new LocalServerConfiguration(configuration));

                await server.StartupAsync(_context).ShouldBeSuccess();
                var sessionData = new LocalContentServerSessionData(testName, Capabilities.ContentOnly, ImplicitPin.PutAndGet, pins: null);
                var createSessionResult = await server.CreateSessionAsync(new OperationContext(_context), sessionData, cacheName);
                createSessionResult.ShouldBeSuccess();

                (int sessionId, _) = createSessionResult.Value;
                using var sessionReference = server.GetSession(sessionId);
                var session = sessionReference.Session;
                
                // Create a GRPC client to connect to the server
                var port = new MemoryMappedFilePortReader(grpcPortFileName, Logger).ReadPort();
                await _clientCache.UseAsync(new OperationContext(_context), LocalHost, port, async (nestedContext, grpcCopyClient) =>
                {
                    await testAct(server, rootPath, session, grpcCopyClient);
                    return Unit.Void;
                });

                await server.ShutdownAsync(_context).ShouldBeSuccess();
            }
        }

        protected override void Dispose(bool disposing)
        {
            _clientCache.Dispose();

            base.Dispose(disposing);
        }
    }
}
