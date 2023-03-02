// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Hashing.FileSystemHelpers;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Azure.EventHubs;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// The way <see cref="ContentLocationEventDataSerializer"/> should validate serialized entries.
    /// </summary>
    public enum ValidationMode
    {
        /// <summary>
        /// The validation is off.
        /// </summary>
        Off,

        /// <summary>
        /// Trace if the deserialized entries are not the same as the original set.
        /// </summary>
        Trace,

        /// <summary>
        /// Fail if the deserialized entries are not the same as the original set.
        /// </summary>
        Fail,
    }

    /// <summary>
    /// Defines the way the data is serialized.
    /// </summary>
    /// <remarks>
    /// Once the new mode is tested this enum will be removed.
    /// </remarks>
    public enum SerializationMode
    {
        /// <summary>
        /// BxlReader/Writer-based serialization/deserialization
        /// </summary>
        Legacy,

        /// <summary>
        /// Span-based serialization deserialization.
        /// </summary>
        SpanBased,
    }

    /// <summary>
    /// Helper class used for serialization/deserialization of <see cref="ContentLocationEventData"/> instances.
    /// </summary>
    public sealed class ContentLocationEventDataSerializer
    {
        private readonly IAbsFileSystem _fileSystem;
        private readonly SerializationMode _serializationMode;
        private readonly ValidationMode _validationMode;
        private const string Prefix = nameof(ContentLocationEventDataSerializer);
        private static readonly Tracer Tracer = new Tracer(Prefix);

        /// <summary>
        /// Max size of the <see cref="EventData"/> instance.
        /// </summary>
        public const int MaxEventDataPayloadSize = 200 << 10;

        private const int MaxDesiredTraceMessageSize = 4096;

        private readonly StreamBinaryWriter _writer = new StreamBinaryWriter();
        private readonly StreamBinaryReader _reader = new StreamBinaryReader();
        private readonly ObjectPool<BxlArrayBufferWriter<byte>> _arrayBufferWriters = new(() => new BxlArrayBufferWriter<byte>(), bw => bw.Clear());

        private readonly bool _synchronize;

        /// <nodoc />
        public ContentLocationEventDataSerializer(IAbsFileSystem fileSystem, SerializationMode serializationMode, ValidationMode validationMode, bool synchronize = false)
        {
            _fileSystem = fileSystem;
            _serializationMode = serializationMode;
            _validationMode = validationMode;
            _synchronize = synchronize;
        }

        /// <summary>
        /// Saves the <paramref name="messages"/> into <paramref name="path"/>.
        /// </summary>
        public async Task<long> SaveToFileAsync(OperationContext context, AbsolutePath path, IReadOnlyList<ContentLocationEventData> messages, int bufferSizeHint = -1)
        {
            // Not deleting the file in this method in case of serialization exception.
            // The caller of this method does it instead.
            using FileStream stream = _fileSystem.Open(
                path,
                FileAccess.ReadWrite,
                FileMode.Create,
                FileShare.Read | FileShare.Delete,
                FileOptions.None,
                AbsFileSystemExtension.DefaultFileStreamBufferSize).ToFileStream();

            if (_serializationMode == SerializationMode.Legacy)
            {
                using var writer = BuildXLWriter.Create(stream, leaveOpen: true);
                SerializeEvents(writer, messages);
                return stream.Position;
            }

            return await SaveToFileWithSpanAsync(context, stream, messages, bufferSizeHint);
        }

        /// <summary>
        /// Serialize a given <paramref name="message"/> into a pooled byte[] instance.
        /// </summary>
        /// <remarks>
        /// If the <paramref name="sizeHint"/> is provided than that value will be used to get the size of the pooled byte array.
        /// Otherwise the size will be computed based on the estimated message size.
        /// This is done for testing purposes, because we want to make sure that the serialization works even with a small original
        /// buffer size and the implementation will try the bigger buffers to fit the given message.
        /// </remarks>
        internal static (PooledObjectWrapper<byte[]> buffer, int writtenLength) SerializeMessage(OperationContext context, ContentLocationEventData message, int sizeHint = -1)
        {
            sizeHint = sizeHint <= 0 ? (int)(message.EstimateSerializedInstanceSize() * 2) : sizeHint;

            while (true)
            {
                try
                {
                    var bufferHandle = Pools.GetByteArray(sizeHint);
                    var spanWriter = bufferHandle.Instance.AsSpan().AsWriter();
                    message.Serialize(ref spanWriter);
                    return (bufferHandle, spanWriter.WrittenBytes.Length);
                }
                catch (InsufficientLengthException e)
                {
                    Tracer.Warning(context, $"Getting a bigger buffer to accomodate the messages size because the size '{sizeHint}' is insufficient. MinLength={e.MinLength}.");
                    // We still might have more iterations because MinLength can still be small since its computed during writing a particular piece of data
                    // and does not reflect the overall size that will be written to eventually.
                    sizeHint = Math.Max(sizeHint, e.MinLength) * 10;
                }
            }
        }

        private async Task<long> SaveToFileWithSpanAsync(
            OperationContext context,
            FileStream stream,
            IReadOnlyList<ContentLocationEventData> messages,
            int bufferSizeHint)
        {
            long writtenLength = writeMessageCount(stream, messages.Count);
            foreach (var message in messages)
            {
                var (bufferHandle, serializedLength) = SerializeMessage(context, message, bufferSizeHint);
                using (bufferHandle)
                {
                    var buffer = bufferHandle.Instance;
                    await stream.WriteAsync(buffer, offset: 0, count: serializedLength, context.Token);
                    writtenLength += serializedLength;
                }
            }

            return writtenLength;

            static long writeMessageCount(FileStream file, int messageCount)
            {
                using var bufferHandle = Pools.GetByteArray(minimumCapacity: sizeof(int));
                var buffer = bufferHandle.Instance;
                var spanWriter = buffer.AsSpan().AsWriter();

                spanWriter.WriteCompact(messageCount);
                int count = spanWriter.WrittenBytes.Length;

                // Using an API that takes byte[] and not Span<byte> because the span-based API is only available in .net core.
#pragma warning disable AsyncFixer02 // this is a synchronous method
                file.Write(buffer, offset: 0, count: count);
#pragma warning restore AsyncFixer02
                return count;
            }
        }

        /// <summary>
        /// Gets the events from <paramref name="path"/>.
        /// </summary>
        public IReadOnlyList<ContentLocationEventData> LoadFromFile(OperationContext context, AbsolutePath path, bool deleteOnClose = true)
        {
            using var stream = _fileSystem.Open(
                path,
                FileAccess.Read,
                FileMode.Open,
                FileShare.Read | FileShare.Delete,
                deleteOnClose ? FileOptions.DeleteOnClose : FileOptions.None,
                AbsFileSystemExtension.DefaultFileStreamBufferSize).ToFileStream();

            if (_serializationMode == SerializationMode.Legacy)
            {
                using var reader = BuildXLReader.Create(stream, leaveOpen: true);

                // Calling ToList to force materialization of IEnumerable to avoid access of disposed stream.
                return DeserializeEvents(reader).ToList();
            }

            using var handle = MemoryMappedFileHandle.CreateReadOnly(stream, leaveOpen: true);
            return DeserializeEvents(handle.Content);
        }

        /// <nodoc />
        public IReadOnlyList<EventData> Serialize(OperationContext context, IReadOnlyList<ContentLocationEventData> messages)
        {
            return _serializationMode == SerializationMode.Legacy ? SerializeLegacy(context, messages) : SerializeWithSpan(context, messages);
        }

        private IReadOnlyList<EventData> SerializeLegacy(OperationContext context, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    var result = SerializeLegacyCore(context, eventDatas).ToList();

                    if (_validationMode != ValidationMode.Off)
                    {
                        var deserializedEvents = result.SelectMany(e => DeserializeEventsLegacy(e, DateTime.Now)).ToList();
                        AnalyzeEquality(context, eventDatas, deserializedEvents);
                    }

                    return result;
                });
        }

        private IReadOnlyList<EventData> SerializeWithSpan(OperationContext context, IReadOnlyList<ContentLocationEventData> eventDatas)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    var result = SerializeWithSpanCore(context, eventDatas).ToList();

                    if (_validationMode != ValidationMode.Off)
                    {
                        var deserializedEvents = result.SelectMany(e => DeserializeEventsWithSpan(e, DateTime.Now)).ToList();
                        AnalyzeEquality(context, eventDatas, deserializedEvents);
                    }

                    return result;
                });
        }

        private static bool Equal(IReadOnlyList<ContentLocationEventData> originalEventDatas, IReadOnlyList<ContentLocationEventData> deserializedEvents)
        {
            // Flatten the event data for comparison
            var left = originalEventDatas.SelectMany(data => GetIndices(data).Select(index => ((data, index)))).ToList();
            var right = deserializedEvents.SelectMany(data => GetIndices(data).Select(index => (data, index))).ToList();

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!Equal(left[i].data, left[i].index, right[i].data, right[i].index))
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<int> GetIndices(ContentLocationEventData data)
        {
            // Always have at least one index to ensure that events with no hashes like Reconcile are not skipped
            return Enumerable.Range(0, Math.Max(data.ContentHashes.Count, 1));
        }

        private static bool Equal(ContentLocationEventData left, int leftIndex, ContentLocationEventData right, int rightIndex)
        {
            if (left.Kind != right.Kind || left.Sender != right.Sender)
            {
                return false;
            }

            if (left.Kind == EventKind.Blob)
            {
                // Reconcile blobs do not have hashes so just check blob id equality
                var leftReconcile = (BlobContentLocationEventData)left;
                var rightReconcile = (BlobContentLocationEventData)right;
                return leftReconcile.BlobId == rightReconcile.BlobId;
            }

            if (left.ContentHashes.Count == 0 || right.ContentHashes.Count == 0)
            {
                // This is a strange case where an event other than reconcile has zero hashes. Not an error per se.
                // That said, we verify that an empty event data matches when deserialized.
                return left.ContentHashes.Count == right.ContentHashes.Count;
            }

            if (left.ContentHashes[leftIndex] != right.ContentHashes[rightIndex])
            {
                return false;
            }

            switch (left.Kind)
            {
                case EventKind.AddLocation:
                    var leftAdd = (AddContentLocationEventData)left;
                    var rightAdd = (AddContentLocationEventData)right;
                    if (leftAdd.ContentSizes[leftIndex] != rightAdd.ContentSizes[rightIndex])
                    {
                        return false;
                    }
                    break;
            }

            return true;
        }

        private string PrintHashInfo(ContentLocationEventData eventData, int index)
        {
            switch (eventData)
            {
                case AddContentLocationEventData add:
                    return $"{add.ContentHashes[index].ToString()}: {add.ContentSizes[index]}";
                default:
                    return eventData.ContentHashes[index].ToString();
            }
        }

        private string GetTraceInfo(ContentLocationEventData eventData)
        {
            switch (eventData)
            {
                case BlobContentLocationEventData blobEvent:
                    return $"{blobEvent.BlobId}";
                default:
                    return string.Join(", ", Enumerable.Range(0, eventData.ContentHashes.Count).Select(index => PrintHashInfo(eventData, index)));
            }
        }

        private void AnalyzeEquality(OperationContext context, IReadOnlyList<ContentLocationEventData> originalMessages, IReadOnlyList<ContentLocationEventData> deserializedMessages)
        {
            // EventData entries may be split due to the size restriction.
            if (!Equal(originalMessages, deserializedMessages))
            {
                StringBuilder builder = new StringBuilder();
                var header = $"{Prefix}: Serialization equality mismatch detected.";
                builder.AppendLine($"{header} Emitting original event information:");

                foreach (var eventData in originalMessages)
                {
                    builder.AppendLine($"{eventData.Kind}[{eventData.ContentHashes.Count}]({GetTraceInfo(eventData)})");
                }

                builder.AppendLine($"Deserialized event information:");
                foreach (var eventData in deserializedMessages)
                {
                    builder.AppendLine($"{eventData.Kind}[{eventData.ContentHashes.Count}]({GetTraceInfo(eventData)})");
                }

                // Split up printout of original event information so that it doesn't get discarded by the telemetry pipeline
                var totalLength = builder.Length;
                var traceCount = (int)Math.Ceiling((double)totalLength / MaxDesiredTraceMessageSize);

                for (int i = 0; i < traceCount; i++)
                {
                    var start = i * MaxDesiredTraceMessageSize;
                    var length = Math.Min(totalLength - start, MaxDesiredTraceMessageSize);
                    Tracer.Warning(context, $"{i} of {traceCount}: '{builder.ToString(start, length)}'");
                }

                if (_validationMode == ValidationMode.Fail)
                {
                    throw new InvalidOperationException(header);
                }
            }
        }

        private IEnumerable<EventData> SerializeLegacyCore(OperationContext context, IReadOnlyList<ContentLocationEventData> messages)
        {
            var splitLargeInstancesIfNeeded = SplitLargeInstancesIfNeeded(context, messages);

            if (splitLargeInstancesIfNeeded.Count != messages.Count)
            {
                context.TracingContext.Debug($"Split {messages.Count} to {splitLargeInstancesIfNeeded.Count} because of size restrictions.", component: nameof(ContentLocationEventDataSerializer));
            }

            using (_writer.PreservePosition())
            {
                int currentCount = 0;

                while (currentCount < splitLargeInstancesIfNeeded.Count)
                {
                    long oldOffset = _writer.Buffer.Position;

                    splitLargeInstancesIfNeeded[currentCount].Serialize(_writer.Writer);

                    long newOffset = _writer.Buffer.Position;
                    long eventSize = newOffset - oldOffset;

                    Contract.Assert(eventSize <= MaxEventDataPayloadSize, $"No mitigation for single {splitLargeInstancesIfNeeded[currentCount].Kind} event that is too large");
                    bool isLast = currentCount == (splitLargeInstancesIfNeeded.Count - 1);
                    bool isOverflow = newOffset > MaxEventDataPayloadSize;

                    if (isOverflow || isLast)
                    {
                        if (isOverflow)
                        {
                            // Need to change the offset for the overflow case, but not if the last element is serialized.
                            _writer.Buffer.SetLength(oldOffset);
                        }

                        var eventData = new EventData(_writer.Buffer.ToArray());
                        _writer.ResetPosition();

                        yield return eventData;

                        if (!isOverflow)
                        {
                            // Need to break because we don't increment current count
                            yield break;
                        }
                    }
                    else
                    {
                        currentCount++;
                    }
                }
            }
        }

        private IEnumerable<EventData> SerializeWithSpanCore(OperationContext context, IReadOnlyList<ContentLocationEventData> messages)
        {
            var splitLargeInstancesIfNeeded = SplitLargeInstancesIfNeeded(context, messages);

            if (splitLargeInstancesIfNeeded.Count != messages.Count)
            {
                context.TracingContext.Debug($"Split {messages.Count} to {splitLargeInstancesIfNeeded.Count} because of size restrictions.", component: nameof(ContentLocationEventDataSerializer));
            }

            using var handler = _arrayBufferWriters.GetInstance();
            var arrayBufferWriter = handler.Instance;

            int currentCount = 0;

            while (currentCount < splitLargeInstancesIfNeeded.Count)
            {
                int oldOffset = arrayBufferWriter.WrittenCount;
                var spanWriter = new SpanWriter(arrayBufferWriter);
                splitLargeInstancesIfNeeded[currentCount].Serialize(ref spanWriter);

                int newOffset = arrayBufferWriter.WrittenCount;
                int eventSize = newOffset - oldOffset;

                Contract.Assert(
                    eventSize <= MaxEventDataPayloadSize,
                    $"No mitigation for single {splitLargeInstancesIfNeeded[currentCount].Kind} event that is too large");
                bool isLast = currentCount == (splitLargeInstancesIfNeeded.Count - 1);
                bool isOverflow = newOffset > MaxEventDataPayloadSize;

                if (isOverflow || isLast)
                {
                    if (isOverflow)
                    {
                        // Need to change the offset for the overflow case, but not if the last element is serialized.
                        arrayBufferWriter.SetPosition(oldOffset);
                    }

                    var eventData = new EventData(arrayBufferWriter.WrittenSpan.ToArray());

                    // We don't need to clear the buffer, just set the position to the beginning.
                    arrayBufferWriter.SetPosition(0);

                    yield return eventData;

                    if (!isOverflow)
                    {
                        // Need to break because we don't increment current count
                        yield break;
                    }
                }
                else
                {
                    currentCount++;
                }
            }
        }

        /// <nodoc />
        public void SerializeEvents(BuildXLWriter writer, IReadOnlyList<ContentLocationEventData> messages)
        {
            SynchronizeIfNeeded(
                _ =>
                {
                    writer.WriteCompact(messages.Count);
                    foreach (var eventData in messages)
                    {
                        eventData.Serialize(writer);
                    }

                    return Unit.Void;
                });
        }

        /// <nodoc />
        public void SerializeEvents(ref SpanWriter writer, IReadOnlyList<ContentLocationEventData> messages)
        {
            // Not using helpers because we can't capture 'writer' in a delegate.
            if (_synchronize)
            {
                Monitor.Enter(this);
            }

            try
            {
                writer.WriteCompact(messages.Count);
                foreach (var eventData in messages)
                {
                    eventData.Serialize(ref writer);
                }
            }
            finally
            {
                if (_synchronize)
                {
                    Monitor.Exit(this);
                }
            }
        }

        private List<ContentLocationEventData> DeserializeEvents(ReadOnlySpan<byte> content)
        {
            return SynchronizeIfNeeded(
                content,
                static content =>
                {
                    var reader = content.AsReader();
                    var entriesCount = reader.ReadInt32Compact();
                    var result = new List<ContentLocationEventData>();

                    for (int i = 0; i < entriesCount; i++)
                    {
                        // Using default as eventTimeUtc because reconciliation events should not have touches.
                        result.Add(ContentLocationEventData.Deserialize(ref reader, eventTimeUtc: default));
                    }

                    return result;
                });
        }

        private List<ContentLocationEventData> DeserializeEvents(BuildXLReader reader)
        {
            return SynchronizeIfNeeded(_ => deserializeEventsCore().ToList());

            IEnumerable<ContentLocationEventData> deserializeEventsCore()
            {
                var entriesCount = reader.ReadInt32Compact();

                for (int i = 0; i < entriesCount; i++)
                {
                    // Using default as eventTimeUtc because reconciliation events should not have touches.
                    yield return ContentLocationEventData.Deserialize(reader, eventTimeUtc: default);
                }
            }
        }

        /// <nodoc />
        public IReadOnlyList<ContentLocationEventData> DeserializeEvents(EventData message, DateTime? eventTimeUtc = null)
        {
            return _serializationMode == SerializationMode.Legacy
                ? DeserializeEvents(message, eventTimeUtc)
                : DeserializeEventsWithSpan(message, eventTimeUtc);
        }

        /// <nodoc />
        public IReadOnlyList<ContentLocationEventData> DeserializeEventsLegacy(EventData message, DateTime? eventTimeUtc = null)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    if (eventTimeUtc == null)
                    {
                        Contract.Assert(message.SystemProperties != null, "Either eventTimeUtc argument must be provided or message.SystemProperties must not be null. Did you forget to provide eventTimeUtc arguments in tests?");
                        eventTimeUtc = message.SystemProperties.EnqueuedTimeUtc;
                    }

                    var data = message.Body;
                    return _reader.DeserializeSequence(data.AsMemory(), reader => ContentLocationEventData.Deserialize(reader, eventTimeUtc.Value));
                });
        }

        private IReadOnlyList<ContentLocationEventData> DeserializeEventsWithSpan(EventData message, DateTime? eventTimeUtc = null)
        {
            return SynchronizeIfNeeded(
                _ =>
                {
                    if (eventTimeUtc == null)
                    {
                        Contract.Assert(message.SystemProperties != null, "Either eventTimeUtc argument must be provided or message.SystemProperties must not be null. Did you forget to provide eventTimeUtc arguments in tests?");
                        eventTimeUtc = message.SystemProperties.EnqueuedTimeUtc;
                    }

                    var dataReader = message.Body.AsSpan().AsReader();
                    var result = new List<ContentLocationEventData>();
                    while (!dataReader.IsEnd)
                    {
                        result.Add(ContentLocationEventData.Deserialize(ref dataReader, eventTimeUtc.Value));
                    }

                    return result;
                });
        }

        /// <nodoc />
        public static IReadOnlyList<ContentLocationEventData> SplitLargeInstancesIfNeeded(OperationContext context, IReadOnlyList<ContentLocationEventData> source)
        {
            var estimatedSizes = source.Select(e => e.EstimateSerializedInstanceSize()).ToList();
            if (!estimatedSizes.Any(es => es > MaxEventDataPayloadSize))
            {
                // We know that there is no entries with the size that is greater then the max payload size.
                // In this case, just return the original array.
                return source;
            }

            return splitLargeInstancesIfNeededCore().ToList();

            IEnumerable<ContentLocationEventData> splitLargeInstancesIfNeededCore()
            {
                for (int i = 0; i < source.Count; i++)
                {
                    // Splitting the incoming instances only if their estimated size is greater then the max payload size.
                    if (estimatedSizes[i] > MaxEventDataPayloadSize)
                    {
                        foreach (var entry in source[i].Split(context, MaxEventDataPayloadSize))
                        {
                            yield return entry;
                        }
                    }
                    else
                    {
                        yield return source[i];
                    }
                }
            }
        }

        private T SynchronizeIfNeeded<T>(Func<bool, T> func)
        {
            if (_synchronize)
            {
                lock (this)
                {
                    return func(true);
                }
            }
            else
            {
                return func(false);
            }
        }

        private delegate T ParseData<T>(ReadOnlySpan<byte> content);

        private T SynchronizeIfNeeded<T>(ReadOnlySpan<byte> data, ParseData<T> func)
        {
            if (_synchronize)
            {
                lock (this)
                {
                    return func(data);
                }
            }
            else
            {
                return func(data);
            }
        }
    }
}
