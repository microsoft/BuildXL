// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.MetadataService
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class ContentMetadataEventStreamTests : TestBase, IDisposable
    {
        protected Tracer Tracer { get; } = new Tracer(nameof(ContentMetadataEventStreamTests));

        private readonly LocalRedisFixture _redisFixture;

        public ContentMetadataEventStreamTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            Contract.RequiresNotNull(redis);
            _redisFixture = redis;
        }

        [Fact]
        public Task WriteSingleEvent()
        {
            return RunTest(async (context, stream) =>
            {
                var ev1 = new RegisterContentLocationsRequest();

                await stream.WriteEventAsync(context, ev1).SelectResult(r => r.Should().BeTrue());
                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                events.Count.Should().Be(1);
                events[0].MethodId.Should().Be(ev1.MethodId);
            });
        }

        [Fact]
        public Task GarbageCollectSingleEvent()
        {
            return RunTest(async (context, stream) =>
            {
                var ev1 = new RegisterContentLocationsRequest();
                await stream.WriteEventAsync(context, ev1).SelectResult(r => r.Should().BeTrue());

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();
                await stream.AfterCheckpointAsync(context, cursor).ShouldBeSuccess();

                var requests = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                requests.Count.Should().Be(0);
            });
        }

        [Fact]
        public Task WriteAndReadManyEventsSingleBlock()
        {
            var testSize = 100;

            return RunTest(async (context, stream) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                // Wait one after the other to ensure the sequencing is exactly what we expect
                foreach (var request in requests)
                {
                    await stream.WriteEventAsync(context, request).SelectResult(r => r.Should().BeTrue());
                }

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                events.Count.Should().Be(testSize);

                for (var i = 0; i < requests.Length; i++)
                {
                    (events[i] as RegisterContentLocationsRequest).MachineId.Should().BeEquivalentTo(requests[i].MachineId);
                }
            });
        }

        [Fact]
        public Task WriteAndReadManyEventsAcrossBlocks()
        {
            var testSize = 5;
            var logRefreshFrequency = TimeSpan.FromMinutes(5);

            return RunTest(async (context, stream) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                // Wait one after the other to ensure the sequencing is exactly what we expect
                foreach (var request in requests)
                {
                    await stream.WriteEventAsync(context, request).SelectResult(r => r.Should().BeTrue());
                    await stream.CommitLoopIterationAsync(context);
                }

                var cursor = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                var events = await CollectStream(context, stream, cursor).ThrowIfFailureAsync();
                events.Count.Should().Be(testSize);
            }, contentMetadataEventStreamConfiguration: new ContentMetadataEventStreamConfiguration()
            {
                LogBlockRefreshInterval = logRefreshFrequency,
            });
        }

        [Fact]
        public Task WriteAndReadManyEventsAcrossCheckpoints()
        {
            var testSize = 5;

            return RunTest(async (context, stream) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                var cursors = new CheckpointLogId[testSize];
                foreach (var indexed in requests.AsIndexed())
                {
                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());
                    cursors[indexed.Index] = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                    // By not calling AfterCheckpointAsync, we stop GC from happening. This should let us read
                    // all events we have posted
                }

                var events = await CollectStream(context, stream, cursors[0]).ThrowIfFailureAsync();
                events.Count.Should().Be(testSize);
            });
        }

        [Fact]
        public Task WriteAndReadManyEventsAcrossCheckpointsAndBlocks()
        {
            var logRefreshFrequency = TimeSpan.FromMinutes(5);
            var testSize = 5;

            return RunTest(async (context, stream) =>
            {
                var requests = Enumerable.Range(0, testSize).Select(idx => new RegisterContentLocationsRequest()
                {
                    MachineId = new MachineId(idx),
                }).ToArray();

                var cursors = new CheckpointLogId[testSize];
                foreach (var indexed in requests.AsIndexed())
                {
                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());

                    await stream.CommitLoopIterationAsync(context);

                    await stream.WriteEventAsync(context, indexed.Item).SelectResult(r => r.Should().BeTrue());

                    cursors[indexed.Index] = await stream.BeforeCheckpointAsync(context).ThrowIfFailureAsync();

                    // By not calling AfterCheckpointAsync, we stop GC from happening. This should let us read
                    // all events we have posted
                }

                await stream.CommitLoopIterationAsync(context);

                var events = await CollectStream(context, stream, cursors[0]).ThrowIfFailureAsync();
                events.Count.Should().Be(2 * testSize);
            }, contentMetadataEventStreamConfiguration: new ContentMetadataEventStreamConfiguration()
            {
                LogBlockRefreshInterval = logRefreshFrequency,
            });
        }

        private Task<Result<List<ServiceRequestBase>>> CollectStream(OperationContext context, IContentMetadataEventStream stream, CheckpointLogId cursor)
        {
            var msg = $"Cursor=[{cursor}]";
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var events = new List<ServiceRequestBase>();

                await stream.ReadEventsAsync(context, cursor, r =>
                {
                    events.Add(r);
                    return new ValueTask(Task.CompletedTask);
                }).ThrowIfFailureAsync();

                return Result.Success(events);
            }, extraStartMessage: msg, extraEndMessage: _ => msg);
        }

        private async Task RunTest(
            Func<OperationContext, ContentMetadataEventStream, Task> runTestAsync,
            ContentMetadataEventStreamConfiguration contentMetadataEventStreamConfiguration = null)
        {
            var tracingContext = new Context(Logger);
            var operationContext = new OperationContext(tracingContext);

            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_redisFixture, TestGlobal.Logger);

            var volatileEventStorage = new BlobWriteAheadEventStorage(
                new BlobEventStorageConfiguration()
                {
                    Credentials = new Interfaces.Secrets.AzureStorageCredentials(connectionString: storage.ConnectionString),
                });
            
            contentMetadataEventStreamConfiguration ??= new ContentMetadataEventStreamConfiguration();
            var contentMetadataEventStream = new ContentMetadataEventStream(contentMetadataEventStreamConfiguration, volatileEventStorage);

            await contentMetadataEventStream.StartupAsync(operationContext).ThrowIfFailure();
            await contentMetadataEventStream.CompleteOrChangeLogAsync(operationContext, CheckpointLogId.InitialLogId);
            contentMetadataEventStream.Toggle(true);
            await runTestAsync(operationContext, contentMetadataEventStream);
            await contentMetadataEventStream.ShutdownAsync(operationContext).ThrowIfFailure();
        }

        public override void Dispose()
        {
            _redisFixture.Dispose();
        }
    }
}
