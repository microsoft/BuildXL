// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// A special version of <see cref="LocationChangeMachineIdSet"/> that keeps the location entries sorted to allow merging them without deserialization.
    /// </summary>
    public sealed class SortedLocationChangeMachineIdSet : LocationChangeMachineIdSet
    {
        /// <nodoc />
        public static new SortedLocationChangeMachineIdSet EmptyInstance { get; } = new(ImmutableArray<LocationChange>.Empty);

        /// <inheritdoc />
        protected override SetFormat Format => SetFormat.LocationChangeSorted;

        /// <nodoc />
        public SortedLocationChangeMachineIdSet(ImmutableArray<LocationChange> sortedMachineIds)
            : base(sortedMachineIds)
        {
        }

        /// <inheritdoc />
        protected override LocationChangeMachineIdSet Create(in ImmutableArray<LocationChange> locationStates)
        {
            return new SortedLocationChangeMachineIdSet(locationStates);
        }

        /// <inheritdoc />
        protected override void SerializeCore(ref SpanWriter writer)
        {
            // An important difference for the sorted case:
            // the length is not an int written in the compact form, but plain 'ushort'!
            // Its important because the merge without deserialization logic expects it to be short.
            var locationStates = GetSortedLocations();
            writer.Write((ushort)locationStates.Length);
            SerializeLocationChanges(locationStates, ref writer);
        }

        private ImmutableArray<LocationChange> GetSortedLocations()
        {
            // Sort only if necessary.
            var locationStates = LocationStates;
            return locationStates.Length > 1 ? locationStates.Sort(LocationChangeMachineIdComparer.Instance) : locationStates;
        }
        
        internal static MachineIdSet DeserializeCoreSorted(ref SpanReader reader)
        {
            // This version uses 'ushort' as the length
            var count = reader.ReadUInt16();
            var machineIds = new LocationChange[count];

            for (int i = 0; i < count; i++)
            {
                machineIds[i] = new LocationChange(reader.ReadUInt16());
            }

            var immutableMachineIds = Unsafe.As<LocationChange[], ImmutableArray<LocationChange>>(ref machineIds);

            

            return new SortedLocationChangeMachineIdSet(immutableMachineIds);
        }

        [Conditional("DEBUG")]
        private static void AssertSorted(ImmutableArray<LocationChange> machineIds)
        {
            // Checking only in debug because it would be very costly.
            // Plus this check exists only for deserialization case because otherwise we can have non-sorted state in-memory.
            var comparer = LocationChangeMachineIdComparer.Instance;
            Contract.Assert(
                machineIds.SequenceEqual(machineIds.Sort(comparer), comparer),
                $"Given machine ids must be sorted, but was: {string.Join(", ", machineIds)}.");
        }

        internal static bool HasMachineIdCoreSorted(ReadOnlySpan<byte> data, int index)
        {
            var reader = data.AsReader();
            // This version uses 'ushort' as the length
            var count = reader.ReadUInt16();

            for (int i = 0; i < count; i++)
            {
                var locationChange = new LocationChange(reader.ReadUInt16());
                if (locationChange.IsAdd && locationChange.Index == index)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the locations can be merged without deserialization.
        /// </summary>
        public static bool CanMergeSortedLocations(ref SpanReader reader1, ref SpanReader reader2)
        {
            // If the locations were sorted we can merge them in place without deserializing them into the typed objects
            var entry1Format = (SetFormat)reader1.ReadByte();
            var entry2Format = (SetFormat)reader2.ReadByte();
            if (entry1Format != SetFormat.LocationChangeSorted || entry2Format != SetFormat.LocationChangeSorted)
            {
                // Can't merge unsorted locations without full deserialization.
                return false;
            }

            return true;
        }

        /// <summary>
        /// Merge the location changes in-flight without deserialization.
        /// </summary>
        public static void MergeSortedMachineLocationChanges(ref SpanReader reader1, ref SpanReader reader2, ref SpanWriter writer)
        {
            writer.WriteByte((byte)SetFormat.LocationChangeSorted);

            // Then merging the location (without the footer because its only used by GCS)
            // WriteMergeLocations expects the length to be an 'ushort' and not int written in the compact format!
            // That's why when writing sorted locations we need to write the length differently than in a non-sorted case.
            writer.WriteMergeLocations(ref reader1, ref reader2, keepRemoves: true, writeFooter: false);
        }
    }
}
