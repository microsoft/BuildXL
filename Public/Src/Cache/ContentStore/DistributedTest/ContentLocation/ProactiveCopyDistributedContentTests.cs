// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Stores;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Extensions;
using ContentStoreTest.Stores;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class ProactiveCopyDistributedContentTests : LocalLocationStoreDistributedContentTests
    {
        public ProactiveCopyDistributedContentTests(LocalRedisFixture redis, ITestOutputHelper output) : base(redis, output)
        {
        }

        // Setup all grpc ports before creating stores so they know how to talk to each other.
        protected override object PrepareAdditionalCreateStoreArgs(int storeCount)
        {
            return Enumerable.Range(0, storeCount).Select(i => PortExtensions.GetNextAvailablePort()).ToArray();
        }

        protected override bool EnableProactiveCopy => true;

        protected override IContentStore CreateStore(
            Context context,
            TestFileCopier fileCopier,
            ICopyRequester copyRequester,
            DisposableDirectory testDirectory,
            int index,
            bool enableDistributedEviction,
            int? replicaCreditInMinutes,
            bool enableRepairHandling,
            bool emptyFileHashShortcutEnabled,
            object additionalArgs)
        {
            if (index == 0)
            {
                return base.CreateStore(context, fileCopier, copyRequester, testDirectory, index, enableDistributedEviction, replicaCreditInMinutes, enableRepairHandling, emptyFileHashShortcutEnabled, additionalArgs);
            }

            var grpcPortsByStoreIndex = additionalArgs as int[];
            var configuration = new ContentStoreConfiguration();

            var rootPath = testDirectory.Path;
            configuration.Write(FileSystem, rootPath).Wait();

            var grpcPortFileName = Guid.NewGuid().ToString();

            var serviceConfiguration = new ServiceConfiguration(
                new Dictionary<string, AbsolutePath> { { "Default", rootPath } },
                rootPath,
                ServiceConfiguration.DefaultMaxConnections,
                ServiceConfiguration.DefaultGracefulShutdownSeconds,
                grpcPortsByStoreIndex[index],
                grpcPortFileName);

            // HACK: This will only work for 3 machines. We know that master is machine 0, and workes are 1 and 2.
            copyRequester = index == 0 || grpcPortsByStoreIndex.Length < 3
                ? copyRequester // Master does not need a copy requester.
                : new GrpcFileCopier(context, grpcPortsByStoreIndex[index == 1 ? 2 : 1], 16, 1, 1); // Point workers towards each other.

            return new TestInProcessServiceClientContentStore(
                FileSystem,
                Logger,
                "Default",
                grpcPortFileName,
                null,
                serviceConfiguration,
                // Ignore path since we configured only one cache in the testDirectory root.
                contentStoreFactory: _ => new SessionCapturingStore(base.CreateStore(context, fileCopier, copyRequester, testDirectory, index, enableDistributedEviction, replicaCreditInMinutes, enableRepairHandling, emptyFileHashShortcutEnabled, additionalArgs))
                );
        }

        [Fact]
        public async Task ProativeCopyDistributedTest()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            var contentHashes = new List<ContentHash>();

            int machineCount = 3;
            ConfigureWithOneMaster();

            // We need pin better to be triggered.
            PinConfiguration = new PinConfiguration();

            await RunTestAsync(
                loggingContext,
                machineCount,
                async context =>
                {
                    var masterStore = context.GetMaster();
                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    var sessions = context.EnumerateWorkersIndices().Select(i => context.GetDistributedSession(i)).ToArray();

                    // Insert random file #1 into worker #1
                    var putResult1 = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash1 = putResult1.ContentHash;

                    var getBulkResult1 = await masterStore.GetBulkAsync(context, hash1, GetBulkOrigin.Global).ShouldBeSuccess();

                    // LocationStore knew no machines, so copying should not be possible. However, next time it will know location 1.
                    getBulkResult1.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    // Insert random file #2 into worker #2
                    var putResult2 = await sessions[0].PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    var hash2 = putResult2.ContentHash;

                    await Task.Delay(TimeSpan.FromSeconds(1)); // Wait for proactive copy to finish in the background.

                    var getBulkResult2 = await masterStore.GetBulkAsync(context, hash2, GetBulkOrigin.Global).ShouldBeSuccess();

                    // Should have proactively copied to worker #1
                    getBulkResult2.ContentHashesInfo[0].Locations.Count.Should().Be(2);
                },
                implicitPin: ImplicitPin.None);
        }
    }

    internal class SessionCapturingStore : IContentStore
    {
        public IContentStore Inner;
        public IList<IContentSession> ContentSessions { get; set; } = new List<IContentSession>();
        public IList<IReadOnlyContentSession> ReadOnlyContentSessions { get; set; } = new List<IReadOnlyContentSession>();

        public SessionCapturingStore(IContentStore inner)
        {
            Inner = inner;
        }

        public bool StartupCompleted => Inner.StartupCompleted;

        public bool StartupStarted => Inner.StartupStarted;

        public bool ShutdownCompleted => Inner.ShutdownCompleted;

        public bool ShutdownStarted => Inner.ShutdownStarted;

        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            var sessionResult = Inner.CreateReadOnlySession(context, name, implicitPin);
            if (sessionResult.Succeeded)
            {
                ReadOnlyContentSessions.Add(sessionResult.Session);
            }
            return sessionResult;
        }

        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            var sessionResult = Inner.CreateSession(context, name, implicitPin);
            if (sessionResult.Succeeded)
            {
                ContentSessions.Add(sessionResult.Session);
            }
            return sessionResult;
        }

        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash) => Inner.DeleteAsync(context, contentHash);

        public void Dispose() => Inner.Dispose();

        public Task<GetStatsResult> GetStatsAsync(Context context) => Inner.GetStatsAsync(context);

        public void PostInitializationCompleted(Context context, BoolResult result) => Inner.PostInitializationCompleted(context, result);

        public Task<BoolResult> ShutdownAsync(Context context) => Inner.ShutdownAsync(context);

        public Task<BoolResult> StartupAsync(Context context) => Inner.StartupAsync(context);
    }
}
