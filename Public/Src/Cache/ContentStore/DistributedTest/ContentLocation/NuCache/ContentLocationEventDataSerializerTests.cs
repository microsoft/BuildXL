// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.ContentStore.Hashing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.ContractsLight;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEventDataSerializerTests : TestBase
    {
        /// <nodoc />
        public ContentLocationEventDataSerializerTests(ITestOutputHelper output)
        : base(TestGlobal.Logger, output)
        {
        }

        public static IEnumerable<object[]> EventKinds => Enum.GetValues(typeof(EventKind)).OfType<EventKind>().Select(k => new object[] { k, k.ToString() });

        [Theory]
        [MemberData(nameof(EventKinds))]
        public async Task LargeInstanceEventsTest(EventKind kind, string kindName)
        {
            BuildXL.Utilities.Analysis.IgnoreArgument(kindName, "Kind name is only specified so enum value name shows up in test name");
            if (kind == EventKind.Blob)
            {
                // Blob events don't have a large equivalent since they just have a storage id
                return;
            }

            var context = new OperationContext(new Context(Logger));
            var harness = new LargeEventTestHarness();
            var sender = harness.Sender;

            var configuration = new MemoryContentLocationEventStoreConfiguration();
            var memoryEventHub = new MemoryEventHubClient(configuration);
            var eventStore = new EventHubContentLocationEventStore(configuration,
                harness,
                "TestMachine",
                harness,
                TestRootDirectoryPath / "eventwork",
                SystemClock.Instance);

            await eventStore.StartupAsync(context).ThrowIfFailure();

            eventStore.StartProcessing(context, new EventSequencePoint(DateTime.UtcNow)).ThrowIfFailure();

            var serializer = CreateContentLocationEventDataSerializer();

            await sendAndVerifyLargeEvent(kind);

            using var stream = new MemoryStream();
            using var writer = BuildXL.Utilities.BuildXLWriter.Create(stream);

            serializer.SerializeEvents(writer, harness.Events);

            stream.Position.Should().BeGreaterThan(ContentLocationEventDataSerializer.MaxEventDataPayloadSize,
                "Event should be larger than max event payload size to properly test serialization logic");

            bool canSplit = kind == EventKind.AddLocation
                || kind == EventKind.AddLocationWithoutTouching
                || kind == EventKind.RemoveLocation
                || kind == EventKind.RemoveLocation
                || kind == EventKind.Touch;

            foreach (var eventData in harness.Events)
            {
                eventData.SerializationKind.Should().Be(kind);
            }

            configuration.Hub.EventStream.Count.Should().BeGreaterThan(0);
            foreach (var rawEvent in configuration.Hub.EventStream)
            {
                rawEvent.Body.Count.Should().BeLessOrEqualTo(ContentLocationEventDataSerializer.MaxEventDataPayloadSize);
            }

            if (canSplit)
            {
                // Events should be split
                harness.Events.Count.Should().BeGreaterThan(1);
                configuration.Hub.EventStream.Count.Should().BeGreaterThan(1);

                // No uploads/downloads should happen for splittable events
                harness.State.DownloadedCount.Should().Be(0);
                harness.State.UploadedCount.Should().Be(0);
                harness.State.UploadedSize.Should().Be(0);
                harness.State.DownloadedSize.Should().Be(0);
            }
            else
            {
                harness.Events.Count.Should().Be(1);
                configuration.Hub.EventStream.Count.Should().Be(1);

                harness.State.DownloadedCount.Should().Be(1);
                harness.State.UploadedCount.Should().Be(1);
                harness.State.UploadedSize.Should().BeGreaterOrEqualTo(stream.Position);
                harness.State.UploadedSize.Should().Be(harness.State.DownloadedSize);
            }

            async Task sendAndVerifyLargeEvent(EventKind kind)
            {
                const int largeEventContentCount = 50000;

                switch (kind)
                {
                    case EventKind.AddLocation:
                    case EventKind.AddLocationWithoutTouching:
                    {
                        var sent = Enumerable.Range(0, largeEventContentCount).Select(_ => new ContentHashWithSize(ContentHash.Random(), ThreadSafeRandom.Generator.Next(0, 100000))).ToList();

                        eventStore.AddLocations(
                            context,
                            sender,
                            sent,
                            touch: kind == EventKind.AddLocation).ThrowIfFailure();

                        var received = harness.Events.Cast<AddContentLocationEventData>().SelectMany(e => e.ContentHashes.SelectList((hash, index) => (hash, size: e.ContentSizes[index]))).ToList();

                        received.Count.Should().Be(sent.Count);

                        for (int i = 0; i < received.Count; i++)
                        {
                            received[i].hash.Should().Be(new ShortHash(sent[i].Hash));
                            received[i].size.Should().Be(sent[i].Size);
                        }

                        return;
                    }

                    case EventKind.RemoveLocation:
                    {
                        var sent = Enumerable.Range(0, largeEventContentCount).Select(_ => ContentHash.Random()).ToList();

                        eventStore.RemoveLocations(
                            context,
                            sender,
                            sent).ThrowIfFailure();

                        var received = harness.Events.Cast<RemoveContentLocationEventData>().SelectMany(e => e.ContentHashes).ToList();

                        received.Count.Should().Be(sent.Count);

                        for (int i = 0; i < received.Count; i++)
                        {
                            received[i].Should().Be(new ShortHash(sent[i]));
                        }

                        return;
                    }
                    case EventKind.Touch:
                    {
                        var sent = Enumerable.Range(0, largeEventContentCount).Select(_ => ContentHash.Random()).ToList();

                        eventStore.Touch(
                            context,
                            sender,
                            sent,
                            DateTime.UtcNow).ThrowIfFailure();

                        var received = harness.Events.Cast<TouchContentLocationEventData>().SelectMany(e => e.ContentHashes).ToList();

                        received.Count.Should().Be(sent.Count);

                        for (int i = 0; i < received.Count; i++)
                        {
                            received[i].Should().Be(new ShortHash(sent[i]));
                        }

                        return;
                    }
                    case EventKind.UpdateMetadataEntry:
                    {
                        var contentHashes = Enumerable.Range(0, largeEventContentCount).Select(_ => ContentHash.Random()).ToArray();
                        var sent = new UpdateMetadataEntryEventData(
                            sender,
                            StrongFingerprint.Random(),
                            new MetadataEntry(
                                new ContentHashListWithDeterminism(
                                    new ContentHashList(contentHashes),
                                    CacheDeterminism.None),
                                DateTime.UtcNow.ToFileTimeUtc()));

                        await eventStore.UpdateMetadataEntryAsync(context, sent).ShouldBeSuccess();

                        var received = harness.Events.Cast<UpdateMetadataEntryEventData>().SelectMany(e => e.Entry.ContentHashListWithDeterminism.ContentHashList.Hashes).ToList();

                        received.Count.Should().Be(contentHashes.Length);

                        for (int i = 0; i < received.Count; i++)
                        {
                            received[i].Should().Be(contentHashes[i]);
                        }

                        return;
                    }
                    default:
                        throw Contract.AssertFailure($"No large event for kind {kind}. Add large event to ensure this case is tested");
                }
            }
        }

        [Fact]
        public void LargeInstanceIsSplitAutomatically()
        {
            const int numberOfHashesPerItem = 20000;
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var largeMessage = GenerateRandomEventData(0, numberOfHashesPerItem, touchTime);
            var serializedMessages = serializer.Serialize(OperationContext(), new[] { largeMessage }).ToList();

            // Round trip validation is performed by the serializer
            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().NotBe(1);
        }

        [Fact]
        public void TwoHunderdEventsShouldBeSerializedIntoOneEventDAta()
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var largeMessage = Enumerable.Range(1, 200).Select<int, ContentLocationEventData>(n => GenerateRandomEventData(0, numberOfHashes: 2, touchTime: touchTime)).ToArray();
            var serializedMessages = serializer.Serialize(OperationContext(), largeMessage).ToList();

            // Round trip validation is performed by the serializer
            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().Be(1);
        }

        [Fact]
        public void CreateEventDatasReturnsMoreThanOneValueDueToTheSize()
        {
            const int numberOfItems = 1000;
            const int numberOfHashesPerItem = 200;
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var messages = Enumerable.Range(1, numberOfItems).Select<int, ContentLocationEventData>(n => GenerateRandomEventData(n, numberOfHashesPerItem, touchTime)).ToArray();

            // Round trip validation is performed by the serializer
            var serializedMessages = serializer.Serialize(OperationContext(), messages).ToList();

            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().NotBe(1);
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(10, 2)]
        public void SerializationRoundtripWithSmallNumberOfElements(int numberOfItems, int numberOfHashesPerItem)
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var messages = Enumerable.Range(1, numberOfItems).Select<int, ContentLocationEventData>(n => GenerateRandomEventData(n, numberOfHashesPerItem, touchTime)).ToArray();

            // Round trip validation is performed by the serializer
            var serializedMessages = serializer.Serialize(OperationContext(), messages).ToList();
            serializedMessages.Count.Should().Be(1); // All the cases we have should fit into one message.
        }

        private static ContentLocationEventData GenerateRandomEventData(int index, int numberOfHashes, DateTime touchTime)
        {
            var random = new Random(index);
            var hashesAndSizes = Enumerable.Range(1, numberOfHashes).Select(n => (hash: new ShortHash(ContentHash.Random()), size: (long)random.Next(10_000_000))).ToList();
            return (index % 3) switch
            {
                0 => (ContentLocationEventData)new AddContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), hashesAndSizes.SelectArray(n => n.size)),
                1 => new TouchContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), touchTime),
                _ => new RemoveContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash)),
            };
        }

        private static ContentLocationEventDataSerializer CreateContentLocationEventDataSerializer() => new ContentLocationEventDataSerializer(ValidationMode.Fail);

        private static OperationContext OperationContext() => new OperationContext(new Context(TestGlobal.Logger));

        private class LargeEventTestHarness : CentralStorage, IContentLocationEventHandler
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(LargeEventTestHarness));

            public InnerState State { get; set; }

            public List<ContentLocationEventData> Events => State.Events;

            internal class InnerState
            {
                public List<ContentLocationEventData> Events { get; } = new List<ContentLocationEventData>();
                public Dictionary<string, byte[]> Storage { get; } = new Dictionary<string, byte[]>();
                public long UploadedSize = 0;
                public long UploadedCount = 0;
                public long DownloadedSize = 0;
                public long DownloadedCount = 0;
            }

            public MachineId Sender { get; } = new MachineId(23);

            public LargeEventTestHarness()
            {
                ResetState();
            }

            public void ResetState()
            {
                State = new InnerState();
            }

            public void ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime)
            {
                Events.Add(new TouchContentLocationEventData(sender, hashes, accessTime.ToDateTime()));
            }

            public void LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime)
            {
                Events.Add(new AddContentLocationEventData(sender, hashes, touch: updateLastAccessTime) { Reconciling = reconciling });
            }

            public void LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling)
            {
                Events.Add(new RemoveContentLocationEventData(sender, hashes) { Reconciling = reconciling });
            }

            public void MetadataUpdated(OperationContext context, StrongFingerprint strongFingerprint, MetadataEntry entry)
            {
                Events.Add(new UpdateMetadataEntryEventData(Sender, strongFingerprint, entry));
            }

            protected override Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader, bool isImmutable)
            {
                return BoolResult.SuccessTask;
            }

            protected override Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
            {
                var bytes = State.Storage[storageId];
                File.WriteAllBytes(targetFilePath.Path, bytes);
                Interlocked.Increment(ref State.DownloadedCount);
                Interlocked.Add(ref State.DownloadedSize, bytes.Length);
                return BoolResult.SuccessTask;
            }

            protected override Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string name, bool garbageCollect = false)
            {
                var bytes = File.ReadAllBytes(file.Path);
                Interlocked.Add(ref State.UploadedSize, bytes.Length);
                Interlocked.Increment(ref State.UploadedCount);
                State.Storage[name] = bytes;
                return Task.FromResult(Result.Success(name));
            }
        }
    }
}
