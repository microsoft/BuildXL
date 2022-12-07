// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Microsoft.Azure.Amqp.Framing;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache;

internal static class ContentLocationEntryTestHelpers
{
    public static MachineIdSet CreateMachineIdSet(IEnumerable<MachineId> machineIds)
    {
        return MachineIdSet.Empty.SetExistence(MachineIdCollection.Create(machineIds.ToList()), exists: true);
    }

    public static ContentLocationEntry MergeTest(this ContentLocationEntry left, ContentLocationEntry right)
    {
        var merge = left.Merge(right, sortLocations: true);
        var merge2 = MergeViaDeserialization(left, right);

        merge2.Should().Be(merge, "Manual merge should be equivalent to the deserialization-based one.");
        return merge;
    }

    public static ContentLocationEntry MergeViaDeserialization(ContentLocationEntry left, ContentLocationEntry right)
    {
        var reader1 = left.WithSortedLocations().AsSpan().AsReader();
        var reader2 = right.WithSortedLocations().AsSpan().AsReader();

        byte[] mergedData = new byte[4 * 1024];
        var mergeWriter = mergedData.AsSpan().AsWriter();
        bool merged = ContentLocationEntry.TryMergeSortedLocations(ref reader1, ref reader2, ref mergeWriter);
        merged.Should().BeTrue();

        // Deserializing the entry back.
        return ContentLocationEntry.Deserialize(mergedData.AsSpan(start: 0, length: mergeWriter.Position));
    }

    public static ContentLocationEntry WithSortedLocations(this ContentLocationEntry entry)
    {
        Contract.Requires(entry.Locations is LocationChangeMachineIdSet || entry.Locations.Count == 0, $"Type: {entry.Locations.GetType()}");
        return ContentLocationEntry.Create(
            new SortedLocationChangeMachineIdSet((entry.Locations as LocationChangeMachineIdSet)?.LocationStates ?? ImmutableArray<LocationChange>.Empty),
            entry.ContentSize,
            entry.LastAccessTimeUtc,
            entry.CreationTimeUtc);
    }

    public static LocationChangeMachineIdSet CreateChangeSet(bool exists, params MachineId[] machineIds) =>
        MachineIdSet.CreateChangeSet(sortLocations: false, exists, machineIds);

    public static ContentLocationEntry CloneWithStream(this ContentLocationEntry source)
    {
        using (var memoryStream = new MemoryStream())
        {
            using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
            {
                source.Serialize(writer);
            }

            memoryStream.Position = 0;
            using (var reader = BuildXLReader.Create(memoryStream, leaveOpen: true))
            {
                return ContentLocationEntry.Deserialize(reader);
            }
        }
    }

    public static ContentLocationEntry CloneWithSpan(this ContentLocationEntry source)
    {
        var reader = AsSpan(source).AsReader();
        return ContentLocationEntry.Deserialize(ref reader);
    }

    public static TMachineIdSet CloneWithSpan<TMachineIdSet>(this TMachineIdSet source) where TMachineIdSet : MachineIdSet
    {
        var reader = AsSpan(source).AsReader();
        return (TMachineIdSet)MachineIdSet.Deserialize(ref reader);
    }

    public static ReadOnlySpan<byte> AsSpan(this ContentLocationEntry source)
    {
        var buffer = new byte[10 * 1024]; // Should be enough.
        var writer = buffer.AsSpan().AsWriter();
        source.Serialize(ref writer);
        return buffer.AsSpan(start: 0, length: writer.Position);
    }

    public static ReadOnlySpan<byte> AsSpan(this MachineIdSet source)
    {
        var buffer = new byte[10 * 1024]; // Should be enough.
        var writer = buffer.AsSpan().AsWriter();
        source.Serialize(ref writer);
        return buffer.AsSpan(start: 0, length: writer.Position);
    }

    public static LocationChangeMachineIdSet ToChangeMachineIdSetIfNeeded(this MachineIdSet source)
    {
        if (source is SortedLocationChangeMachineIdSet r)
        {
            return r;
        }

        if (source is LocationChangeMachineIdSet lc)
        {
            return new SortedLocationChangeMachineIdSet(lc.LocationStates);
        }

        return MachineIdSet.CreateChangeSet(sortLocations: true, exists: true, source.EnumerateMachineIds().ToArray());
    }

    public static LocationChangeMachineIdSet MergeTest(this MachineIdSet left, MachineIdSet right)
    {
        var merge = left.Merge(right, sortLocations: true);

        var comparer = LocationChangeMachineIdSet.LocationChangeMachineIdComparer.Instance;

        left = left.ToChangeMachineIdSetIfNeeded();
        right = right.ToChangeMachineIdSetIfNeeded();

        // Serialization process will sort the locations, we don't need to do that manually here.
        var leftData = left.AsSpan().AsReader();
        var rightData = right.AsSpan().AsReader();

        byte[] output = new byte[10 * 1024];
        var mergeWriter = output.AsSpan().AsWriter();

        SortedLocationChangeMachineIdSet.CanMergeSortedLocations(ref leftData, ref rightData).Should().BeTrue();
        SortedLocationChangeMachineIdSet.MergeSortedMachineLocationChanges(ref leftData, ref rightData, ref mergeWriter);

        var mergeReader = output.AsSpan(start: 0, length: mergeWriter.Position).AsReader();
        var sortedMerge = (object)MachineIdSet.Deserialize(ref mergeReader);

        // Need to sort the merge result manually in order to compare that the results are the same.
        var manuallySorted = new SortedLocationChangeMachineIdSet(merge.LocationStates.Sort(comparer));
        sortedMerge.Should().Be(manuallySorted, "Sorted merge result must be the same as in-memory merge result.");
        return merge;
    }
}
