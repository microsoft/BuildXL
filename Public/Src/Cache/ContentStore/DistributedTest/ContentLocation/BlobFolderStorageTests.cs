// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Utilities.Tracing;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    public class BlobFolderStorageTests : TestWithOutput
    {
        private class BlobFolderStorageConfiguration : IBlobFolderStorageConfiguration
        {
            public AzureBlobStorageCredentials? Credentials { get; set; } = AzureBlobStorageCredentials.StorageEmulator;

            public string ContainerName { get; set; } = "blobfolderstoragetests";

            public string FolderName { get; set; }

            public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromMinutes(1);

            public RetryPolicyConfiguration RetryPolicy { get; set; } = BlobFolderStorage.DefaultRetryPolicy;

            public BlobFolderStorageConfiguration()
            {
                // Use a random folder every time to avoid clashes
                FolderName = Guid.NewGuid().ToString();
            }
        }

        private readonly LocalRedisFixture _fixture;

        public BlobFolderStorageTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        [Fact]
        public Task CreatesMissingContainerOnWrite()
        {
            return RunTest(async (context, storage, clock) =>
                {
                    var file = new BlobName("ThisIsATest", IsRelative: true);
                    await storage.WriteAsync(context, file, "Test").ShouldBeSuccess();

                    var r = await storage.EnsureContainerExists(context).ShouldBeSuccess();
                    r.Value.Should().BeFalse();
                }, elideStartup: true);
        }

        [Fact]
        public Task DoesNotCreateMissingContainerOnRead()
        {
            return RunTest(async (context, storage, clock) =>
            {
                var file = new BlobName("ThisIsATest", IsRelative: true);
                var r = await storage.ReadStateAsync<string>(context, file).ShouldBeSuccess();
                r.Value!.Value.Should().BeNull();

                var cr = await storage.EnsureContainerExists(context).ShouldBeSuccess();
                cr.Value.Should().BeTrue();
            }, elideStartup: true);
        }

        [Theory]
        [InlineData(10, 10, 60)] // This typically takes <30s
        [InlineData(1024, 1, 180)] // This typically takes <1m
        public Task ConcurrentReadModifyWriteEventuallyFinishes(int numTasks, int numIncrementsPerTask, double maxDurationSeconds)
        {
            var maxTestDuration = TimeSpan.FromSeconds(maxDurationSeconds);

            return RunTest(async (context, storage, clock) =>
            {

                var blob = new BlobName("race.json", IsRelative: true);

                var started = 0;
                var startSemaphore = new SemaphoreSlim(0, numTasks + 1);

                await storage.WriteAsync<int>(context, blob, 0).ShouldBeSuccess();

                var tasks = new Task[numTasks];
                for (var i = 0; i < numTasks; i++)
                {
                    tasks[i] = Task.Run(async () =>
                    {
                        Interlocked.Increment(ref started);
                        await startSemaphore.WaitAsync();

                        for (var j = 0; j < numIncrementsPerTask; j++)
                        {
                            await storage.ReadModifyWriteAsync<int>(context, blob, state => state + 1).ShouldBeSuccess();
                        }
                    });
                }

                // Wait until they all start, and release them all at once
                while (started < numTasks)
                {
                    await Task.Delay(1);
                }

                // Perform experiment
                var stopwatch = StopwatchSlim.Start();
                startSemaphore.Release(numTasks);
                await Task.WhenAll(tasks);
                var elapsed = stopwatch.Elapsed;

                // Ensure value is what we expected it to be
                var r = await storage.ReadAsync<int>(context, blob).ShouldBeSuccess();
                r.Value.Should().Be(numTasks * numIncrementsPerTask);

                // Ensure time taken is what we expected it to be
                elapsed.Should().BeLessOrEqualTo(maxTestDuration);
            },
            timeout: maxTestDuration);
        }

        private Task RunTest(Func<OperationContext, BlobFolderStorage, IClock, Task> runTest, IClock? clock = null, BlobFolderStorageConfiguration? configuration = null, bool elideStartup = false, TimeSpan? timeout = null, [CallerMemberName] string? caller = null)
        {
            clock ??= SystemClock.Instance;
            timeout ??= Timeout.InfiniteTimeSpan;

            var tracer = new Tracer(caller ?? nameof(BlobFolderStorageTests));
            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);

            // This is here just so we display the text run duration in the logs
            return context.PerformOperationWithTimeoutAsync(
                tracer,
                async context =>
                {
                    using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);

                    configuration ??= new BlobFolderStorageConfiguration();
                    configuration.Credentials = new AzureBlobStorageCredentials(connectionString: storage.ConnectionString);
                    var blobFolderStorage = new BlobFolderStorage(tracer, configuration);

                    if (!elideStartup)
                    {
                        await blobFolderStorage.StartupAsync(context).ThrowIfFailureAsync();
                    }

                    await runTest(context, blobFolderStorage, clock);

                    if (!elideStartup)
                    {
                        await blobFolderStorage.ShutdownAsync(context).ThrowIfFailureAsync();
                    }

                    return BoolResult.Success;
                },
                timeout: timeout.Value,
                traceOperationStarted: false,
                caller: caller ?? nameof(BlobFolderStorageTests)).ThrowIfFailureAsync();
        }
    }
}
