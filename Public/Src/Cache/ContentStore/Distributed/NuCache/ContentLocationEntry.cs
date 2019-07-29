// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Location information for a piece of content.
    /// </summary>
    public sealed class ContentLocationEntry
    {
        /// <nodoc />
        public const int BytesInFileSize = sizeof(long);

        /// <nodoc />
        private static readonly byte[] UnknownSizeBytes = new byte[BytesInFileSize];

        /// <nodoc />
        public const int BitsInFileSize = BytesInFileSize * 8;

        private readonly UnixTime? _creationTimeUtc;

        /// <summary>
        /// True if the entry is a special "missing entry".
        /// </summary>
        public bool IsMissing => LastAccessTimeUtc == default;

        /// <summary>
        /// Returns a set of locations for a given content.
        /// </summary>
        public MachineIdSet Locations { get; }

        /// <summary>
        /// Content size in bytes.
        /// </summary>
        public long ContentSize { get; }

        /// <summary>
        /// Last access time for a current entry.
        /// </summary>
        /// <remarks>
        /// Unlike other properties of this type, this property is not obtained from the remote store.
        /// </remarks>
        public UnixTime LastAccessTimeUtc { get; }

        /// <summary>
        /// Returns time when the entry was created (if provided) or last access time otherwise.
        /// </summary>
        public UnixTime CreationTimeUtc => _creationTimeUtc ?? LastAccessTimeUtc;

        /// <nodoc />
        private ContentLocationEntry(MachineIdSet locations, long contentSize, UnixTime lastAccessTimeUtc, UnixTime? creationTimeUtc)
        {
            Locations = locations;
            ContentSize = contentSize;
            LastAccessTimeUtc = lastAccessTimeUtc;
            _creationTimeUtc = creationTimeUtc;
        }

        /// <summary>
        /// Factory method that creates a valid content location.
        /// </summary>
        public static ContentLocationEntry Create(MachineIdSet locations, long contentSize, UnixTime lastAccessTimeUtc, UnixTime? creationTimeUtc = null)
        {
            Contract.Requires(lastAccessTimeUtc != default);

            return new ContentLocationEntry(locations, contentSize, lastAccessTimeUtc, creationTimeUtc);
        }

        /// <summary>
        /// Returns a special "missing" entry.
        /// </summary>
        public static ContentLocationEntry Missing { get; } = new ContentLocationEntry(MachineIdSet.Empty, -1, default, default);

        /// <summary>
        /// Creates a content location entry from the given data array
        /// </summary>
        public static ContentLocationEntry FromRedisValue(byte[] data, UnixTime lastAccessTime, bool missingSizeHandling = false)
        {
            Contract.Requires(data != null);
            Contract.Requires(data.Length >= BytesInFileSize);

            return Create(locations: new BitMachineIdSet(data, BytesInFileSize), contentSize: ExtractContentSizeFromRedisValue(data, missingSizeHandling), lastAccessTime);
        }

        /// <summary>
        /// Creates a content location entry from the given data array
        /// </summary>
        public static ContentLocationEntry TryCreateFromRedisValue(byte[] data, UnixTime lastAccessTime)
        {
            if (data == null || data.Length < BytesInFileSize)
            {
                return null;
            }

            return FromRedisValue(data, lastAccessTime);
        }

        /// <summary>
        /// Serializes an instance into a binary stream.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(ContentSize);
            Locations.Serialize(writer);
            writer.Write(CreationTimeUtc);
            long lastAccessTimeOffset = LastAccessTimeUtc.Value - CreationTimeUtc.Value;
            writer.WriteCompact(lastAccessTimeOffset);
        }

        /// <summary>
        /// Builds an instance from a binary stream.
        /// </summary>
        public static ContentLocationEntry Deserialize(BuildXLReader reader)
        {
            var size = reader.ReadInt64Compact();
            var locations = MachineIdSet.Deserialize(reader);
            var creationTimeUtc = reader.ReadUnixTime();
            var lastAccessTimeOffset = reader.ReadInt64Compact();
            var lastAccessTime = new UnixTime(creationTimeUtc.Value + lastAccessTimeOffset);
            return Create(locations, size, lastAccessTime, creationTimeUtc);
        }

        /// <nodoc />
        public ContentLocationEntry SetMachineExistence(IReadOnlyCollection<MachineId> machines, bool exists, UnixTime? lastAccessTime = null, long? size = null)
        {
            var locations = Locations.SetExistence(machines, exists);
            if ((lastAccessTime == null || lastAccessTime.Value.Value <= LastAccessTimeUtc.Value)
                && locations.Count == Locations.Count
                && (size == null || ContentSize >= 0))
            {
                return this;
            }

            return new ContentLocationEntry(locations, size ?? ContentSize, lastAccessTime ?? LastAccessTimeUtc, CreationTimeUtc);
        }

        /// <nodoc />
        public ContentLocationEntry Touch(UnixTime accessTime)
        {
            return new ContentLocationEntry(Locations, ContentSize, accessTime > LastAccessTimeUtc ? accessTime : LastAccessTimeUtc, CreationTimeUtc);
        }

        /// <summary>
        /// Extracts a content size from the given content hash info byte array
        /// </summary>
        private static long ExtractContentSizeFromRedisValue(byte[] contentHashInfo, bool missingSizeHandling)
        {
            long size = 0;
            if (BitConverter.IsLittleEndian)
            {
                for (int i = 0; i < sizeof(long); i++)
                {
                    size <<= 8;
                    size |= contentHashInfo[i];
                }
            }
            else
            {
                size = BitConverter.ToInt64(contentHashInfo, 0);
            }

            if (missingSizeHandling)
            {
                // When this is enabled, stored size is greater than actual size by 1 so that
                // unset size is treated as -1.
                size--;
            }

            return size;
        }

        /// <summary>
        /// Creates a binary Redis value representation of an entry with the given size and machine id set.
        /// </summary>
        public static byte[] ConvertSizeAndMachineIdToRedisValue(long size, MachineId machineId)
        {
            var sizeBytes = size >= 0 ? ConvertSizeToRedisRangeBytes(size) : UnknownSizeBytes;
            var machineIdSet = BitMachineIdSet.EmptyInstance.SetExistenceBits(new[] { machineId }, true);

            return CollectionUtilities.ConcatAsArray(sizeBytes, machineIdSet.Data);
        }

        /// <summary>
        /// Converts the size to bytes for storage in redis value
        /// </summary>
        public static byte[] ConvertSizeToRedisRangeBytes(long size)
        {
            Contract.Requires(size >= 0);

            // Increment size so that unset size (all zeros) can be treated as -1 when reading
            // entry. See ExtractContentSizeFromRedisValue with missingSizeHandling=true.
            size++;

            byte[] bytes = BitConverter.GetBytes(size);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return IsMissing ? "Missing location" : $"Count: {Locations.Count}, Size: {ContentSize}, Accessed at: {LastAccessTimeUtc}";
        }
    }
}
