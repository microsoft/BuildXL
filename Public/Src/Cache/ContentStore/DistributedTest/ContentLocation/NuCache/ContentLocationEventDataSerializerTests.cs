// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Cache.ContentStore.Hashing;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using System.Diagnostics.ContractsLight;
using System.Collections.Generic;
using System.Diagnostics;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using System.IO;
using System.Threading;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Serialization;
using Azure.Messaging.EventHubs;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEventDataSerializerTests : TestBase
    {
        /// <nodoc />
        public ContentLocationEventDataSerializerTests(ITestOutputHelper output)
        : base(TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task SerializeWithSpansAndSmallInitialFileSize()
        {
            var context = OperationContext();
            var path = TestRootDirectoryPath / "Tmp.txt";
            var messages = new[] { GenerateRandomEventData(0, numberOfHashes: 50_000, DateTime.Now) };
            var oldSerializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization: true);

            var serializedSize = await oldSerializer.SaveToFileAsync(context, path, messages, bufferSizeHint: 1);
            serializedSize.Should().BeGreaterThan(1); // This means that we had to re-opened the memory-mapped file during serialization.
        }

        [Fact]
        public async Task TaskTestFileSerializationDeserializationBackwardCompatibility()
        {
            const int largeEventContentCount = 0;
            var context = OperationContext();
            var path = TestRootDirectoryPath / "Tmp.txt";
            var messages = new []
                           {
                               // GenerateRandomEventData(0, numberOfHashes: largeEventContentCount, DateTime.Now),
                               // Not using touches because the touch time is not serialized.
                               // GenerateRandomEventData(1, numberOfHashes: largeEventContentCount, DateTime.Now),
                               GenerateRandomEventData(2, numberOfHashes: largeEventContentCount, DateTime.Now),
                               //GenerateRandomEventData(3, numberOfHashes: largeEventContentCount, DateTime.Now),
                           }.ToList();

            var legacySerializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization: false);
            var newSerializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization: true);
            var legacyFileSize = await legacySerializer.SaveToFileAsync(context, path, messages);

            var deserialized = legacySerializer.LoadFromFile(context, path, deleteOnClose: false);

            // Checking the old deserialization
            // Saving to the file doesn't split the large events.
            deserialized.Count.Should().Be(messages.Count);
            deserialized.Should().BeEquivalentTo(messages);

            // The new deserialization.
            deserialized = newSerializer.LoadFromFile(context, path, deleteOnClose: false);

            deserialized.Should().BeEquivalentTo(messages);

            // The new serialization + deserialization
            var newFileSize = await newSerializer.SaveToFileAsync(context, path, messages);
            newFileSize.Should().Be(legacyFileSize);

            deserialized = newSerializer.LoadFromFile(context, path, deleteOnClose: false);
            deserialized.Should().BeEquivalentTo(messages);
        }

        [Theory(Skip = "For manual testing only")]
        [InlineData(true)]
        [InlineData(false)]
        public async Task BenchmarkSerialization(bool useSpanBasedSerialization)
        {
            var context = OperationContext();
            var path = TestRootDirectoryPath / "Tmp.txt";
            var messages = new[] { GenerateRandomEventData(0, numberOfHashes: 50_000, DateTime.Now) };
            var serializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization, ValidationMode.Off);

            // Warming up the check.
            await serializer.SaveToFileAsync(context, path, messages);

            var sw = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                await serializer.SaveToFileAsync(context, TestRootDirectoryPath / $"{i}.txt", messages);
            }

            var writeDuration = sw.ElapsedMilliseconds;
            sw = Stopwatch.StartNew();

            for (int i = 0; i < 1000; i++)
            {
                var pathToRead = TestRootDirectoryPath / $"{i}.txt";
                serializer.LoadFromFile(context, pathToRead);
            }

            // Failing to show the message more easily.
            true.Should().BeFalse($"Mode: {(useSpanBasedSerialization ? "Span-based" : "Legacy")}, WriteDuration: {writeDuration}ms, ReadDuration: {sw.ElapsedMilliseconds}ms");
        }
        
        public static IEnumerable<object[]> EventKinds => Enum.GetValues(typeof(EventKind)).OfType<EventKind>().Select(k => new object[] { k, k.ToString() });

        [Theory]
        [MemberData(nameof(EventKinds))]
        public async Task LargeInstanceEventsTest(EventKind kind, string kindName)
        {
            BuildXL.Utilities.Core.Analysis.IgnoreArgument(kindName, "Kind name is only specified so enum value name shows up in test name");
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

            var arrayBuffer = serializeIntoBuffer(serializer, harness.Events);

            arrayBuffer.WrittenCount.Should().BeGreaterThan(ContentLocationEventDataSerializer.MaxEventDataPayloadSize,
                "Event should be larger than max event payload size to properly test serialization logic");

            bool canSplit = kind == EventKind.AddLocation
                || kind == EventKind.AddLocationWithoutTouching
                || kind == EventKind.RemoveLocation
                || kind == EventKind.Touch;

            foreach (var eventData in harness.Events)
            {
                eventData.SerializationKind.Should().Be(kind);
            }

            configuration.Hub.EventStream.Count.Should().BeGreaterThan(0);
            foreach (var rawEvent in configuration.Hub.EventStream)
            {
                rawEvent.Body.Length.Should().BeLessOrEqualTo(ContentLocationEventDataSerializer.MaxEventDataPayloadSize);
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
                harness.State.UploadedSize.Should().BeGreaterOrEqualTo(arrayBuffer.WrittenCount);
                harness.State.UploadedSize.Should().Be(harness.State.DownloadedSize);
            }

            static BxlArrayBufferWriter<byte> serializeIntoBuffer(ContentLocationEventDataSerializer serializer, List<ContentLocationEventData> events)
            {
                var arrayBuffer = new BxlArrayBufferWriter<byte>();
                var writer = new SpanWriter(arrayBuffer);
                serializer.SerializeEvents(ref writer, events);
                return arrayBuffer;
            }

            async Task sendAndVerifyLargeEvent(EventKind kind)
            {
                const int largeEventContentCount = 50_000;

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
                            sent.SelectList(c => c.ToShortHash())).ThrowIfFailure();

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
                                DateTime.UtcNow));

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
            var serializedMessages = Serialize(serializer, new[] { largeMessage });

            // Round trip validation is performed by the serializer
            Output.WriteLine($"Number of serialized records: {serializedMessages.Count}");
            serializedMessages.Count.Should().NotBe(1);
        }

        [Fact]
        public void TwoHunderdEventsShouldBeSerializedIntoOneEventData()
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            var largeMessage = Enumerable.Range(1, 200).Select<int, ContentLocationEventData>(n => GenerateRandomEventData(0, numberOfHashes: 2, touchTime: touchTime)).ToArray();
            var serializedMessages = Serialize(serializer, largeMessage);

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

            var serializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization: false);
            var messages = Enumerable.Range(1, numberOfItems).Select<int, ContentLocationEventData>(n => GenerateRandomEventData(n, numberOfHashesPerItem, touchTime)).ToArray();

            // Round trip validation is performed by the serializer
            var serializedMessages = Serialize(serializer, messages);

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
            var serializedMessages = Serialize(serializer, messages);
            serializedMessages.Count.Should().Be(1); // All the cases we have should fit into one message.
        }

        [Fact]
        public void SerializationRoundtripWithTouch()
        {
            DateTime touchTime = DateTime.UtcNow;

            var serializer = CreateContentLocationEventDataSerializer();
            ShortHash.TryParse("VSO0:84ABF22908660978EE63C9", out var hash).Should().BeTrue();
            var message = new TouchContentLocationEventData(42.AsMachineId(), new ShortHash[] {hash}, touchTime);

            // Round trip validation is performed by the serializer
            var serializedMessages = Serialize(serializer, new [] { message });
            serializedMessages.Count.Should().Be(1); // All the cases we have should fit into one message.
        }

        [Fact]
        public void SerializationRoundtripWithMetadata()
        {
            var serializer = CreateContentLocationEventDataSerializer();
            DateTime eventTime = DateTime.Now;
            
            var metadataEntry = new MetadataEntry(
                new ContentHashListWithDeterminism(
                    new ContentHashList(
                        contentHashes: new ContentHash[] { ContentHash.Random() },
                        payload: ContentHash.Random().ToByteArray()),
                    CacheDeterminism.SinglePhaseNonDeterministic),
                lastAccessTimeUtc: eventTime);
            var message = new UpdateMetadataEntryEventData(42.AsMachineId(), StrongFingerprint.Random(), metadataEntry);

            // Round trip validation is performed by the serializer
            var serializedMessages = Serialize(serializer, new [] { message });
            serializedMessages.Count.Should().Be(1); // All the cases we have should fit into one message.

            var deserialized = serializer.DeserializeEvents(default, serializedMessages[0], eventTime);
            deserialized.Count.Should().Be(1);
            deserialized[0].Should().Be(message);
        }

        [Fact]
        public async Task SerializationRoundtripWithMetadataLarge()
        {
            var context = OperationContext();
            var path = TestRootDirectoryPath / "Tmp.txt";
            var legacyPath = TestRootDirectoryPath / "Tmp.txt";

            var serializer = CreateContentLocationEventDataSerializer();
            var legacySerializer = CreateContentLocationEventDataSerializer(useSpanBasedSerialization: false);

            var message = GenerateMetadataEvent(42.AsMachineId(), 50_000);

            var length1 = await serializer.SaveToFileAsync(context, path, new[] {message});
            var length2 = await legacySerializer.SaveToFileAsync(context, legacyPath, new[] {message});
            length2.Should().Be(length1);

            var data = FileSystem.ReadAllBytes(path);
            var data2 = FileSystem.ReadAllBytes(legacyPath);
            data.AsSpan().SequenceEqual(data2).Should().BeTrue();
            var deserialized = serializer.LoadFromFile(context, path);
            deserialized.Count.Should().Be(1);
            deserialized[0].Should().Be(message);
        }

        private static List<EventData> Serialize(ContentLocationEventDataSerializer serializer, IReadOnlyList<ContentLocationEventData> messages)
        {
            return serializer.Serialize(OperationContext(), messages).ToList();
        }

        private static ContentLocationEventData GenerateRandomEventData(int index, int numberOfHashes, DateTime touchTime)
        {
            var random = new Random(index);
            var hashesAndSizes = Enumerable.Range(1, numberOfHashes).Select(n => (hash: new ShortHash(ContentHash.Random()), size: (long)random.Next(10_000_000))).ToList();
            return (index % 4) switch
            {
                0 => (ContentLocationEventData)new AddContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), hashesAndSizes.SelectArray(n => n.size)),
                1 => new TouchContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash), touchTime),
                2 => GenerateMetadataEvent(new MachineId(index), numberOfHashes),
                _ => new RemoveContentLocationEventData(new MachineId(index), hashesAndSizes.SelectArray(n => n.hash)),
            };
        }

        private static ContentLocationEventData GenerateMetadataEvent(MachineId sender, int largeEventContentCount)
        {
            var contentHashes = Enumerable.Range(0, largeEventContentCount).Select(_ => ContentHash.Random()).ToArray();
            return new UpdateMetadataEntryEventData(
                sender,
                StrongFingerprint.Random(),
                new MetadataEntry(
                    new ContentHashListWithDeterminism(
                        new ContentHashList(contentHashes),
                        CacheDeterminism.None),
                    DateTime.UtcNow));
        }

        private ContentLocationEventDataSerializer CreateContentLocationEventDataSerializer(
            bool useSpanBasedSerialization = true,
            ValidationMode validationMode = ValidationMode.Fail) => new ContentLocationEventDataSerializer(
            FileSystem,
            useSpanBasedSerialization ? SerializationMode.SpanBased : SerializationMode.Legacy,
            validationMode);

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

            public long ContentTouched(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, UnixTime accessTime)
            {
                Events.Add(new TouchContentLocationEventData(sender, hashes, accessTime.ToDateTime()));
                return hashes.Count;
            }

            public long LocationAdded(OperationContext context, MachineId sender, IReadOnlyList<ShortHashWithSize> hashes, bool reconciling, bool updateLastAccessTime)
            {
                Events.Add(new AddContentLocationEventData(sender, hashes, touch: updateLastAccessTime) { Reconciling = reconciling });
                return hashes.Count;
            }

            public long LocationRemoved(OperationContext context, MachineId sender, IReadOnlyList<ShortHash> hashes, bool reconciling)
            {
                Events.Add(new RemoveContentLocationEventData(sender, hashes) { Reconciling = reconciling });
                return hashes.Count;
            }

            public long MetadataUpdated(OperationContext context, StrongFingerprint strongFingerprint, MetadataEntry entry)
            {
                Events.Add(new UpdateMetadataEntryEventData(Sender, strongFingerprint, entry));
                return 1;
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
