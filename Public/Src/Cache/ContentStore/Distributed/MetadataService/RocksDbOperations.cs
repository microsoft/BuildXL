// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;
using RocksDbSharp;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    /// <summary>
    /// Defines write/get/merge operations for content location entries in
    /// <see cref="RocksDbContentMetadataDatabase"/> columns:
    /// <see cref="RocksDbContentMetadataDatabase.Columns.SstMergeContent"/> and
    /// <see cref="RocksDbContentMetadataDatabase.Columns.MergeContent"/>
    /// </summary>
    public static class RocksDbOperations
    {
        /// <summary>
        /// Gets the entry key from the given short hash
        /// </summary>
        public static ShardHash AsEntryKey(this ShortHash hash)
        {
            return new ShardHash(hash);
        }

        /// <summary>
        /// Deletes the range of keys in the given partition (i.e. all keys with partition as prefix).
        /// </summary>
        public static void DeleteLocationEntryPartitionRange<TWriter>(this TWriter writer, PartitionId partition)
            where TWriter : IRocksDbColumnWriter
        {
            // Create a key after all keys with the given partition by taking partition
            // and suffixing with byte.MaxValue greater to maximum key length.
            // Next partition id can't be used because there is no way to represent the next for partition 255.
            Span<byte> rangeEnd = stackalloc byte[ShortHash.SerializedLength + 2];
            rangeEnd[0] = partition.EndValue;
            rangeEnd.Slice(1).Fill(byte.MaxValue);

            writer.DeleteRange(stackalloc[] { partition.StartValue }, rangeEnd);
        }

        /// <summary>
        /// Gets a key for the partition record in the partition's range after all valid shard hash
        /// keys but within the range which would be deleted by a DeleteRange operation.
        /// </summary>
        public static ReadOnlyArray<byte> GetPartitionRecordKey(this PartitionId partition)
        {
            var key = new byte[ShortHash.SerializedLength + 1];
            key[0] = partition.EndValue;
            key.AsSpan().Slice(1).Fill(byte.MaxValue);
            key[ShortHash.SerializedLength] = 0;

            return key;
        }

        /// <summary>
        /// Delete the entry with the given hash to the database
        /// </summary>
        public static void DeleteLocationEntry<TWriter>(this TWriter writer, ShardHash hash)
            where TWriter : IRocksDbColumnWriter
        {
            writer.Delete(MemoryMarshal.AsBytes(stackalloc[] { hash }));
        }

        /// <summary>
        /// Writes a merge content location entry to the database
        /// </summary>
        public static void MergeLocationEntry<TWriter>(this TWriter db, ShortHash hash, MachineId? machineId, MachineContentInfo info, bool isRemove = false)
            where TWriter : IRocksDbColumnWriter
        {
            ReadOnlySpan<LocationChange> machines = machineId == null
                ? stackalloc LocationChange[0]
                : stackalloc[] { LocationChange.Create(machineId.Value, isRemove: isRemove) };
            MergeOrPutContentLocationEntry(db, AsEntryKey(hash), machines, static c => c, info, merge: true);
        }

        /// <summary>
        /// Writes a merge or put content location entry to the database
        /// </summary>
        public static void MergeOrPutContentLocationEntry<TWriter, T>(
            TWriter db,
            ShardHash hash,
            ReadOnlySpan<T> machineEntries,
            Func<T, LocationChange> getMachine,
            MachineContentInfo info = default,
            bool merge = false)
            where TWriter : IRocksDbColumnWriter
        {
            // 50 is chosen here as a gross overestimate of the additional data from MachineContentInfo
            // In theory 16 bytes would be sufficient, but better safe than sorry.
            SpanWriter value = stackalloc byte[50 + machineEntries.Length * Unsafe.SizeOf<LocationChange>()];

            value.WriteLocationEntry(machineEntries, getMachine, info);

            if (merge)
            {
                db.Merge(key: MemoryMarshal.AsBytes(stackalloc[] { hash }), value: value.WrittenBytes);
            }
            else
            {
                db.Put(key: MemoryMarshal.AsBytes(stackalloc[] { hash }), value: value.WrittenBytes);
            }
        }

        /// <summary>
        /// Writes a content location entry to the span writer
        /// </summary>
        public static void WriteLocationEntry<T>(
            this ref SpanWriter value,
            ReadOnlySpan<T> machineEntries,
            Func<T, LocationChange> getMachine,
            MachineContentInfo info = default)
        {
            value.Write<ushort>((ushort)machineEntries.Length);
            foreach (var entry in machineEntries)
            {
                value.Write<LocationChange>(getMachine(entry));
            }

            info.WriteTo(ref value);
        }

        /// <summary>
        /// Reads out the locations and info
        /// </summary>
        public static void ReadMergedContentLocationEntry(
            ReadOnlySpan<byte> value,
            out ReadOnlySpan<LocationChange> machines,
            out MachineContentInfo info)
        {
            if (value.Length == 0)
            {
                machines = default;
                info = default;
                return;
            }

            SpanReader reader = value;
            var locationCount = reader.Read<ushort>();

            machines = reader.Read<LocationChange>(locationCount);
            info = MachineContentInfo.Read(ref reader);
        }

        /// <summary>
        /// Processes a single merge entry for a merge operator
        /// </summary>
        public static bool ProcessSingleLocationEntry(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, MergeResult result)
        {
            return MergeLocations(key, value, default, result);
        }

        /// <summary>
        /// Merges two content location entry values and writes the result to the result buffer
        /// </summary>
        public static bool MergeLocations(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value1, ReadOnlySpan<byte> value2, MergeResult result)
        {
            result.ValueBuffer.Resize(value1.Length + value2.Length);
            SpanWriter mergedValue = result.ValueBuffer.Value;

            // Full merge excludes removes since it is guaranteed that there are no prior entries to merge
            mergedValue.WriteMergeLocations(value1, value2, keepRemoves: !result.IsFullMerge);
            result.ValueBuffer.Resize(mergedValue.Position);
            return true;
        }

        /// <summary>
        /// Merges two content location entry values and writes the result to the span writer
        /// </summary>
        internal static void WriteMergeLocations(this ref SpanWriter mergeWriter, ReadOnlySpan<byte> value1, ReadOnlySpan<byte> value2, bool keepRemoves = true)
        {
            SpanReader reader1 = value1;
            SpanReader reader2 = value2;
            // This version is used only by GCS that requires writing a footer.
            WriteMergeLocations(ref mergeWriter, ref reader1, ref reader2, keepRemoves, writeFooter: true);
        }

        /// <summary>
        /// Merges two content location entry values and writes the result to the span writer
        /// </summary>
        internal static void WriteMergeLocations(this ref SpanWriter mergeWriter, ref SpanReader reader1, ref SpanReader reader2, bool keepRemoves, bool writeFooter)
        {
            // Capture (copy) writer before writing location count
            // in order to write location count after locations have been merged
            var locationCountWriter = mergeWriter;
            void writeLocation(ref SpanWriter writer, LocationChange location)
            {
                if (location.IsAdd || keepRemoves)
                {
                    writer.Write(location);
                }
            }

            mergeWriter.Write<ushort>(0); // Write placeholder for location count

            // locations1 contains earlier values
            // locations2 contains later values
            var locations1 = reader1.Read<LocationChange>(reader1.Read<ushort>()).GetEnumerator();
            var locations2 = reader2.Read<LocationChange>(reader2.IsEnd ? 0 : reader2.Read<ushort>()).GetEnumerator();

            bool hasValue1 = locations1.MoveNext();
            bool hasValue2 = locations2.MoveNext();

            var mergedLocationsStartPosition = mergeWriter.Position;

            while (hasValue1 || hasValue2)
            {
                if (!hasValue1)
                {
                    writeLocation(ref mergeWriter, locations2.Current);
                    hasValue2 = locations2.MoveNext();
                }
                else if (!hasValue2)
                {
                    writeLocation(ref mergeWriter, locations1.Current);
                    hasValue1 = locations1.MoveNext();
                }
                else
                {
                    var compareResult = locations1.Current.Index.Compare(locations2.Current.Index);
                    if (compareResult == CompareResult.Equal)
                    {
                        // Latest value wins value wins
                        writeLocation(ref mergeWriter, locations2.Current);

                        hasValue1 = locations1.MoveNext();
                        hasValue2 = locations2.MoveNext();
                    }
                    else if (compareResult == CompareResult.RightGreater)
                    {
                        writeLocation(ref mergeWriter, locations1.Current);
                        hasValue1 = locations1.MoveNext();
                    }
                    else
                    {
                        writeLocation(ref mergeWriter, locations2.Current);
                        hasValue2 = locations2.MoveNext();
                    }
                }
            }

            var locationCount = (ushort)((mergeWriter.Position - mergedLocationsStartPosition) / Unsafe.SizeOf<LocationChange>());
            locationCountWriter.Write(locationCount);

            // Only write info footer if entry has locations
            if (locationCount > 0 && writeFooter)
            {
                // Read footers from both, merge them, and write new footer
                var mergedFooter = MachineContentInfo.Merge(MachineContentInfo.Read(ref reader1), MachineContentInfo.Read(ref reader2));
                mergedFooter.WriteTo(ref mergeWriter);
            }
        }
    }
}
