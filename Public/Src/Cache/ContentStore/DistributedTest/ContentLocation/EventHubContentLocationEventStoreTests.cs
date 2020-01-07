using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    public sealed class EventHubContentLocationEventStoreTests : TestBase
    {
        public EventHubContentLocationEventStoreTests(ILogger? logger = null, ITestOutputHelper? output = null)
            : base(logger ?? TestGlobal.Logger, output)
        {
        }

        [Fact]
        public Task ShutdownDoesNotProcessEnqueuedEvents()
        {
            // This test is "weird" in the sense that it relies on a "controlled" race condition. We need to check that
            // the master does not wait for events to finish processing to complete its shutdown. The way we do this is
            // force an arbitrary delay when processing events, and then make sure that we haven't processed any after
            // shutdown.
            //
            // * If we do it with a cancellation token on shutdown started, then we have a race condition, because the
            //   token will be triggered, and the item could be processed before shutdown finishes.
            // * If we do it with a separate lock, we'll need some sort of delay which will be equivalent to what we do
            //   now.
            // * We can't use a cancellation token on shutdown finished, because shutdown awaits for the action blocks
            //   to complete, and this means we have a deadlock.
            //
            // By doing things this way, we are relying on the fact that it is unlikely for a thread to not run for a
            // second. If we assume that's true, then the slowdown will resume after shutdown has started waiting, and
            // we will do the appropriate fast-return path.
            var configuration = new SlowedContentLocationEventStoreConfiguration()
            {
                Slowdown = TimeSpan.FromSeconds(1)
            };

            var eventHandler = new TestEventHandler();
            return WithContentLocationEventStore(async (tracingContext, clock, fileSystem, eventStore) =>
            {
                var context = new OperationContext(tracingContext);
                var sequencePoint = new EventSequencePoint(clock.UtcNow);

                eventStore.StartProcessing(context, sequencePoint).ShouldBeSuccess();

                eventStore.AddLocations(context, MachineId.FromIndex(0), new[] { new ContentHashWithSize(ContentHash.Random(), 1) }).ShouldBeSuccess();

                (await eventStore.ShutdownAsync(context)).ShouldBeSuccess();

                eventHandler.EventsHandled.Should().Be(0);
            }, configuration, eventHandler);
        }

        private class TestEventHandler : IContentLocationEventHandler
        {
            private long _eventsHandled;
            public long EventsHandled => _eventsHandled;

            public void ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime)
            {
                Interlocked.Increment(ref _eventsHandled);
            }

            public void LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime)
            {
                Interlocked.Increment(ref _eventsHandled);
            }

            public void LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling)
            {
                Interlocked.Increment(ref _eventsHandled);
            }
        }

        private async Task WithContentLocationEventStore(Func<Context, IClock, IAbsFileSystem, ContentLocationEventStore, Task> action, ContentLocationEventStoreConfiguration configuration, IContentLocationEventHandler eventHandler, string localMachineName = "Worker")
        {
            string centralStateKeyBase = "ThisIsUnused";

            var clock = new MemoryClock();
            using var fileSystem = new PassThroughFileSystem(TestGlobal.Logger);
            var tracingContext = new Context(TestGlobal.Logger);

            {
                using var localDiskCentralStoreWorkingDirectory = new DisposableDirectory(fileSystem);
                var localDiskCentralStoreConfiguration = new LocalDiskCentralStoreConfiguration(localDiskCentralStoreWorkingDirectory.Path, centralStateKeyBase);
                var centralStorage = new LocalDiskCentralStorage(localDiskCentralStoreConfiguration);

                {
                    using var eventHubWorkingDirectory = new DisposableDirectory(fileSystem);
                    var eventStore = CreateEventStore(configuration, eventHandler, localMachineName, centralStorage, eventHubWorkingDirectory);

                    (await eventStore.StartupAsync(tracingContext)).ShouldBeSuccess();
                    await action(tracingContext, clock, fileSystem, eventStore);
                    if (!eventStore.ShutdownStarted)
                    {
                        (await eventStore.ShutdownAsync(tracingContext)).ShouldBeSuccess();
                    }
                }
            }
        }

        private static ContentLocationEventStore CreateEventStore(
            ContentLocationEventStoreConfiguration configuration,
            IContentLocationEventHandler eventHandler,
            string localMachineName,
            LocalDiskCentralStorage centralStorage,
            DisposableDirectory eventHubWorkingDirectory)
        {
            return configuration switch
            {
                SlowedContentLocationEventStoreConfiguration _ =>
                    new SlowedEventHubContentLocationEventStore(configuration, eventHandler, localMachineName, centralStorage, eventHubWorkingDirectory.Path),
                _ =>
                    ContentLocationEventStore.Create(configuration, eventHandler, localMachineName, centralStorage, eventHubWorkingDirectory.Path),
            };
        }

        private class SlowedContentLocationEventStoreConfiguration : MemoryContentLocationEventStoreConfiguration
        {
            public TimeSpan Slowdown { get; set; } = TimeSpan.Zero;

            public SlowedContentLocationEventStoreConfiguration() : base()
            {
                MaxEventProcessingConcurrency = 2;
            }
        }

        private class SlowedEventHubContentLocationEventStore : EventHubContentLocationEventStore
        {
            private readonly SlowedContentLocationEventStoreConfiguration _configuration;

            public SlowedEventHubContentLocationEventStore(
                ContentLocationEventStoreConfiguration configuration,
                IContentLocationEventHandler eventHandler,
                string localMachineName,
                CentralStorage centralStorage,
                AbsolutePath workingDirectory)
                : base(configuration, eventHandler, localMachineName, centralStorage, workingDirectory)
            {
                Contract.Requires(configuration is SlowedContentLocationEventStoreConfiguration);
                _configuration = (configuration as SlowedContentLocationEventStoreConfiguration)!;
            }

            protected override async Task ProcessEventsCoreAsync(ProcessEventsInput input, ContentLocationEventDataSerializer eventDataSerializer)
            {
                await Task.Delay(_configuration.Slowdown);
                await base.ProcessEventsCoreAsync(input, eventDataSerializer);
            }
        }
    }
}
