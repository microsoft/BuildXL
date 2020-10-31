// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming
{
    /// <summary>
    /// Type of an event happened in the system.
    /// </summary>
    public enum EventKind
    {
        /// <nodoc />
        AddLocation,

        /// <nodoc />
        RemoveLocation,

        /// <nodoc />
        Touch,

        /// <nodoc />
        Blob,

        /// <nodoc />
        AddLocationWithoutTouching,

        /// <nodoc />
        UpdateMetadataEntry,
    }

    /// <summary>
    /// Base class that describe the event.
    /// </summary>
    public abstract class ContentLocationEventData : IEquatable<ContentLocationEventData>
    {
        // Need some wiggle room for auxiliary instance data needed for assessing serialized instance size.
        private const int SerializedSizeBase = 128;

        /// <summary>
        /// Indicates whether an Add/Remove event is related to reconciliation.
        /// NOTE: This is not (de)serialized, but is determined when specifically handling a reconciliation event.
        /// </summary>
        public bool Reconciling { get; internal set; }

        /// <nodoc />
        public EventKind Kind { get; }

        /// <nodoc />
        protected internal virtual EventKind SerializationKind => Kind;

        /// <summary>
        /// A current machine id.
        /// </summary>
        public MachineId Sender { get; }

        /// <summary>
        /// A list of affected content hashes.
        /// </summary>
        /// <remarks>
        /// The list is not null or empty.
        /// </remarks>
        public IReadOnlyList<ShortHash> ContentHashes { get; }

        /// <nodoc />
        protected ContentLocationEventData(EventKind kind, MachineId sender, IReadOnlyList<ShortHash> contentHashes)
        {
            Contract.Requires(contentHashes != null);

            Kind = kind;
            Sender = sender;
            ContentHashes = contentHashes;
        }

        /// <nodoc />
        public static ContentLocationEventData Deserialize(BuildXLReader reader, DateTime eventTimeUtc)
        {
            Contract.Requires(reader != null);

            var kind = (EventKind)reader.ReadByte();
            var sender = MachineId.Deserialize(reader);
            var hashes = reader.ReadReadOnlyList(r => r.ReadShortHash());

            switch (kind)
            {
                case EventKind.AddLocation:
                case EventKind.AddLocationWithoutTouching:
                    return new AddContentLocationEventData(sender, hashes, reader.ReadReadOnlyList(r => r.ReadInt64Compact()), touch: kind == EventKind.AddLocation);
                case EventKind.RemoveLocation:
                    return new RemoveContentLocationEventData(sender, hashes);
                case EventKind.Touch:
                    return new TouchContentLocationEventData(sender, hashes, eventTimeUtc);
                case EventKind.Blob:
                    return new BlobContentLocationEventData(sender, reader.ReadString());
                case EventKind.UpdateMetadataEntry:
                    return new UpdateMetadataEntryEventData(sender, reader);
                default:
                    throw new ArgumentOutOfRangeException($"Unknown event kind '{kind}'.");
            }
        }

        /// <nodoc />
        public virtual void Serialize(BuildXLWriter writer)
        {
            writer.Write((byte)SerializationKind);
            Sender.Serialize(writer);
            writer.WriteReadOnlyList(ContentHashes, (w, hash) => w.Write(hash));

            switch (this) {
                case AddContentLocationEventData addContentLocationEventData:
                    writer.WriteReadOnlyList(addContentLocationEventData.ContentSizes, (w, size) => w.WriteCompact(size));
                    break;
                case RemoveContentLocationEventData removeContentLocationEventData:
                case TouchContentLocationEventData touchContentLocationEventData:
                    // Do nothing. No extra data. Touch timestamp is taken from event enqueue time
                    break;
                case BlobContentLocationEventData reconcileContentLocationEventData:
                    writer.Write(reconcileContentLocationEventData.BlobId);
                    break;
            }
        }

        /// <summary>
        /// Returns an estimated size of the instance
        /// </summary>
        public long EstimateSerializedInstanceSize()
        {
            switch (this)
            {
                case AddContentLocationEventData addContentLocationEventData:
                    return SerializedSizeBase + ContentHashes.Count * ShortHash.SerializedLength + addContentLocationEventData.ContentSizes.Count * sizeof(long);
                default:
                    return SerializedSizeBase + ContentHashes.Count * ShortHash.SerializedLength;
            }
        }

        /// <summary>
        /// Splits the current instance into a smaller instances
        /// </summary>
        public IReadOnlyList<ContentLocationEventData> Split(OperationContext context, long maxEstimatedSize)
        {
            // First, need to compute the number of hashes that will fit into maxEstimatedSize.
            var maxHashCount = maxContentHashesCount(maxEstimatedSize);

            // Then we need to split the instance into a sequence of instances.
            var hashes = ContentHashes.Split(maxHashCount).ToList();
            var result = new List<ContentLocationEventData>(hashes.Count);

            Contract.Assert(hashes.Sum(c => c.Count) == ContentHashes.Count);

            switch (this)
            {
                case AddContentLocationEventData addContentLocationEventData:
                    var contentSizes = addContentLocationEventData.ContentSizes.Split(maxHashCount).ToList();
                    Contract.Assert(hashes.Count == contentSizes.Count);

                    result.AddRange(hashes.Select((t, index) => new AddContentLocationEventData(Sender, t, contentSizes[index], addContentLocationEventData.Touch)));
                    break;
                case RemoveContentLocationEventData _:
                    result.AddRange(hashes.Select(t => new RemoveContentLocationEventData(Sender, t)));
                    break;
                case TouchContentLocationEventData touchContentLocationEventData:
                    result.AddRange(hashes.Select(t => new TouchContentLocationEventData(Sender, t, touchContentLocationEventData.AccessTime)));
                    break;
            }

            foreach (var r in result)
            {
                var estimatedSize = r.EstimateSerializedInstanceSize();
                if (estimatedSize > maxEstimatedSize)
                {
                    context.TraceDebug($"An estimated size is '{estimatedSize}' is greater then the max size '{maxEstimatedSize}' for event '{r.Kind}'.", component: nameof(ContentLocationEventData));
                }
            }

            return result;

            int maxContentHashesCount(long estimatedSize)
            {
                switch (this)
                {
                    case AddContentLocationEventData _:
                        return (int)(estimatedSize - SerializedSizeBase) / (ShortHash.SerializedLength + sizeof(long));
                    default:
                        return (int)(estimatedSize - SerializedSizeBase) / (ShortHash.SerializedLength);
                }
            }
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return EqualityComparer<ContentLocationEventData>.Default.Equals(this, obj as ContentLocationEventData);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (Kind.GetHashCode(), Sender.GetHashCode(), ContentHashes.SequenceHashCode()).GetHashCode();
        }

        /// <nodoc />
        public virtual bool Equals(ContentLocationEventData other)
        {
            return Kind == other.Kind && Sender == other.Sender && ContentHashes.SequenceEqual(other.ContentHashes);
        }
    }

    /// <nodoc />
    public sealed class AddContentLocationEventData : ContentLocationEventData
    {
        /// <summary>
        /// A list of content sizes associated with <see cref="ContentLocationEventData.ContentHashes"/>.
        /// </summary>
        public IReadOnlyList<long> ContentSizes { get; }

        /// <summary>
        /// Whether or not to extend the lifetime of the content.
        /// </summary>
        public bool Touch { get; }

        /// <inheritdoc />
        protected internal override EventKind SerializationKind => Touch ? EventKind.AddLocation : EventKind.AddLocationWithoutTouching;

        /// <nodoc />
        public AddContentLocationEventData(MachineId sender, IReadOnlyList<ShortHashWithSize> addedContent, bool touch = true)
            : this(sender, addedContent.SelectList(c => c.Hash), addedContent.SelectList(c => c.Size), touch)
        {
        }

        /// <nodoc />
        public AddContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes, IReadOnlyList<long> contentSizes, bool touch = true)
            : base(EventKind.AddLocation, sender, contentHashes)
        {
            Contract.Requires(contentSizes != null);
            Contract.Requires(contentSizes.Count == contentHashes.Count);

            ContentSizes = contentSizes;
            Touch = touch;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < ContentHashes.Count; i++)
            {
                sb.Append($"Hash={ContentHashes[i]}, Size={ContentSizes[i]}");
            }

            return $"Event: {Kind}, Sender: {Sender}, Touch: {Touch}, {sb}";
        }

        /// <inheritdoc />
        public override bool Equals(ContentLocationEventData other)
        {
            var rhs = (AddContentLocationEventData)other;
            return base.Equals(other) && (Touch == rhs.Touch) && ContentSizes.SequenceEqual(rhs.ContentSizes);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), ContentSizes.GetHashCode(), Touch).GetHashCode();
        }
    }

    /// <nodoc />
    public sealed class RemoveContentLocationEventData : ContentLocationEventData
    {
        /// <nodoc />
        public RemoveContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes)
            : base(EventKind.RemoveLocation, sender, contentHashes)
        {
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"Event: {Kind}, Sender: {Sender}, {string.Join(", ", ContentHashes)}";
        }
    }

    /// <nodoc />
    public sealed class BlobContentLocationEventData : ContentLocationEventData
    {
        /// <summary>
        /// The blob identifier of the reconciliation blob
        /// </summary>
        public string BlobId { get; }

        /// <nodoc />
        public BlobContentLocationEventData(MachineId sender, string blobId)
            : base(EventKind.Blob, sender, CollectionUtilities.EmptyArray<ShortHash>())
        {
            BlobId = blobId;
        }
    }

    /// <nodoc />
    public sealed class UpdateMetadataEntryEventData : ContentLocationEventData
    {
        /// <summary>
        /// The strong fingerprint key of the content hash list entry
        /// </summary>
        public StrongFingerprint StrongFingerprint { get; }

        /// <summary>
        /// The metadata entry
        /// </summary>
        public MetadataEntry Entry { get; }

        /// <nodoc />
        public UpdateMetadataEntryEventData(MachineId sender, StrongFingerprint strongFingerprint, MetadataEntry entry)
            : base(EventKind.UpdateMetadataEntry, sender, CollectionUtilities.EmptyArray<ShortHash>())
        {
            StrongFingerprint = strongFingerprint;
            Entry = entry;
        }

        /// <nodoc />
        public UpdateMetadataEntryEventData(MachineId sender, BuildXLReader reader)
            : base(EventKind.UpdateMetadataEntry, sender, CollectionUtilities.EmptyArray<ShortHash>())
        {
            StrongFingerprint = StrongFingerprint.Deserialize(reader);
            Entry = MetadataEntry.Deserialize(reader);
        }

        /// <inheritdoc />
        public override void Serialize(BuildXLWriter writer)
        {
            base.Serialize(writer);
            StrongFingerprint.Serialize(writer);
            Entry.Serialize(writer);
        }
    }

    /// <nodoc />
    public sealed class TouchContentLocationEventData : ContentLocationEventData
    {
        /// <nodoc />
        public TouchContentLocationEventData(MachineId sender, IReadOnlyList<ShortHash> contentHashes, DateTime accessTime)
            : base(EventKind.Touch, sender, contentHashes)
        {
            AccessTime = accessTime;
        }

        /// <summary>
        /// Date and time when the content was accessed for the last time.
        /// </summary>
        public DateTime AccessTime { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{base.ToString()}, AccessTime: {AccessTime}";
        }

        /// <inheritdoc />
        public override bool Equals(ContentLocationEventData other)
        {
            var otherTouch = (TouchContentLocationEventData)other;
            return base.Equals(other) && AccessTime == otherTouch.AccessTime;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (base.GetHashCode(), AccessTime.GetHashCode()).GetHashCode();
        }
    }
}
