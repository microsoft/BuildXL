// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// An <see cref="IContentResolver"/> knows how to resolve content locations.
/// </summary>
public interface IContentResolver : IStartupShutdownSlim
{
    /// <summary>
    /// Obtain information about the location of content.
    /// </summary>
    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request);
}

/// <summary>
/// An <see cref="IContentUpdater"/> knows how to update a content tracker with additional information about pieces of
/// content.
/// </summary>
public interface IContentUpdater : IStartupShutdownSlim
{
    /// <summary>
    /// Update the content tracker with additional information about the location of content.
    /// </summary>
    public Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request);
}

/// <summary>
/// The <see cref="IContentTracker"/> is responsible for tracking the location of content in the system. That is, it
/// knows which machines have which pieces of content.
///
/// CODESYNC: <see cref="IGrpcContentTracker"/>
/// </summary>
public interface IContentTracker : IContentResolver, IContentUpdater
{
}

/// <summary>
/// The <see cref="ILocalContentTracker"/> represents an <see cref="IContentTracker"/> that is local to the machine
/// it's running on (i.e., it does not communicate with other machines).
/// </summary>
public interface ILocalContentTracker : IContentTracker
{
    /// <summary>
    /// Each <see cref="ChangeStamp"/> has a sequence number, which is used to determine the order in which operations
    /// happened to be able to order them. When a machine is performing an operation, the <see cref="ChangeStamp"/>
    /// associated with the operation is created by the machine itself. This method returns the highest sequence number
    /// that we have observed for a given machine.
    /// </summary>
    /// <remarks>
    /// A priori, the sequence number can be defined per (Machine, Hash) basis (i.e., each hash in each machine starts
    /// at 0 and is increased every time). In any case, it is extremely important that the sequence number be increased
    /// every time an operation is performed, and that it is never decreased.
    /// </remarks>
    public SequenceNumber GetSequenceNumber(ShortHash shortHash, MachineId machineId);
}

[ProtoContract]
public record GetLocationsRequest
{
    [ProtoMember(1)]
    public IReadOnlyList<ShortHash> Hashes { get; init; } = new List<ShortHash>(capacity: 0);

    [ProtoMember(2)]
    public bool Recursive { get; private init; } = false;

    public override string ToString()
    {
        var entries = string.Join("; ", Hashes.Select(h => h.ToString()));
        return $"{nameof(GetLocationsRequest)} (R={Recursive}) {{ {entries} }}";
    }

    public static GetLocationsRequest SingleHash(ShortHash shortHash, bool recursive = false)
    {
        return new GetLocationsRequest { Hashes = new List<ShortHash>(capacity: 1) { shortHash }, Recursive = recursive };
    }
}

[ProtoContract]
public record GetLocationsResponse
{
    [ProtoMember(1)]
    public IReadOnlyList<ContentEntry> Results { get; init; } = new List<ContentEntry>(capacity: 0);

    public override string ToString()
    {
        var entries = string.Join("; ", Results.Select(h => h.ToString()));
        return $"{nameof(GetLocationsResponse)} {{ {entries} }}";
    }

    /// <summary>
    /// Creates a response having the same structure as the given request, but with all entries unknown. This is the way
    /// we're meant to fail.
    /// </summary>
    public static GetLocationsResponse Empty(GetLocationsRequest request)
    {
        var results = new List<ContentEntry>(capacity: request.Hashes.Count);
        foreach (var hash in request.Hashes)
        {
            results.Add(ContentEntry.Unknown(hash));
        }

        return new GetLocationsResponse { Results = results };
    }

    /// <summary>
    /// Creates a response that is consistent with the given request, by gathering the entries from the given
    /// enumerable into a response.
    /// </summary>
    public static GetLocationsResponse Gather(GetLocationsRequest request, IEnumerable<ContentEntry> entries)
    {
        var groups = new Dictionary<ShortHash, List<ContentEntry>>(capacity: request.Hashes.Count);
        foreach (var entry in entries)
        {
            if (groups.TryGetValue(entry.Hash, out var group))
            {
                group.Add(entry);
            }
            else
            {
                groups[entry.Hash] = new List<ContentEntry>(capacity: 1) { entry };
            }
        }

        var results = new List<ContentEntry>(capacity: request.Hashes.Count);
        foreach (var hash in request.Hashes)
        {
            if (groups.TryGetValue(hash, out var group))
            {
                results.Add(ContentEntry.Merge(group));
            }
            else
            {
                results.Add(ContentEntry.Unknown(hash));
            }
        }

        return new GetLocationsResponse { Results = results, };
    }
}

[ProtoContract]
public record UpdateLocationsRequest
{
    [ProtoMember(1)]
    public IReadOnlyList<ContentEntry> Entries { get; init; } = new List<ContentEntry>(capacity: 0);

    /// <summary>
    /// The concept of TimeToLeave here is the same as in networking. An update may travel only as many hops as it's
    /// TTL allows, and it's used to prevent infinite loops, by ensuring that every forwarding intermediary drops all
    /// updates with TTL = 0.
    ///
    /// <see cref="Hop"/> is used to decrement the TTL.
    /// </summary>
    [ProtoMember(2)]
    public int TimeToLive { get; private init; } = 1;

    public bool Drop => TimeToLive <= 0;

    public override string ToString()
    {
        var entries = string.Join("; ", Entries.Select(h => h.ToString()));
        return $"{nameof(UpdateLocationsRequest)} ({TimeToLive}) {{ {entries} }}";
    }

    public static UpdateLocationsRequest SingleHash(ContentEntry entry, int? timeToLive = null)
    {
        return new UpdateLocationsRequest
        {
            Entries = new List<ContentEntry>(capacity: 1) { entry },
            TimeToLive = timeToLive ?? 1,
        };
    }

    public UpdateLocationsRequest Derived(ContentEntry entry)
    {
        return new UpdateLocationsRequest
        {
            Entries = new List<ContentEntry>(capacity: 1) { entry },
            TimeToLive = TimeToLive
        };
    }

    public static UpdateLocationsRequest FromGetLocationsResponse(GetLocationsResponse response)
    {
        return new UpdateLocationsRequest
        {
            Entries = response.Results
        };
    }

    public UpdateLocationsRequest Hop()
    {
        if (Drop)
        {
            throw new InvalidOperationException("Can't decrement time to live on a dead request");
        }

        return this with { TimeToLive = TimeToLive - 1 };
    }
}

[ProtoContract]
public record ContentEntry
{
    [ProtoMember(1)]
    public ShortHash Hash { get; init; }

    [ProtoMember(2)]
    public long Size { get; init; } = -1;

    [ProtoMember(3)]
    public IReadOnlyList<Stamped<MachineId>> Operations { get; init; } = new List<Stamped<MachineId>>(capacity: 0);

    public override string ToString()
    {
        var operations = string.Join(", ", Operations.Select(o => $"{o.Value}({o.ChangeStamp})"));
        return $"{Hash}:{Size}[{operations}]";
    }

    public IEnumerable<MachineId> Existing()
    {
        return Select(ChangeStampOperation.Add);
    }

    public IEnumerable<MachineId> Tombstones()
    {
        return Select(ChangeStampOperation.Delete);
    }

    private IEnumerable<MachineId> Select(ChangeStampOperation operation)
    {
        return Operations.Where(stamped => stamped.ChangeStamp.Operation == operation).Select(stamped => stamped.Value);
    }

    public bool Contains(MachineId machineId)
    {
        return HasOperation(machineId, ChangeStampOperation.Add);
    }

    public bool Tombstone(MachineId machineId)
    {
        return HasOperation(machineId, ChangeStampOperation.Delete);
    }

    private bool HasOperation(MachineId machineId, ChangeStampOperation operation)
    {
        return Operations.Any(o => o.Value == machineId && o.ChangeStamp.Operation == operation);
    }

    public static ContentEntry Unknown(ShortHash shortHash)
    {
        return new ContentEntry { Hash = shortHash, Size = -1, Operations = new List<Stamped<MachineId>>(capacity: 0) };
    }

    public static ContentEntry FromLastWriterWins(ShortHash shortHash, LastWriterWinsContentEntry entry)
    {
        return new ContentEntry { Hash = shortHash, Size = entry.Size, Operations = entry.Operations.Operations, };
    }

    public static ContentEntry Merge(IReadOnlyList<ContentEntry> entries)
    {
        if (entries.Count == 0)
        {
            throw new ArgumentException("Cannot merge empty list of entries");
        }

        var baseline = LastWriterWinsContentEntry.From(entries[0]);
        foreach (var entry in entries.Skip(1))
        {
            if (entry.Hash != entries[0].Hash)
            {
                throw new ArgumentException("Cannot merge entries with different hashes");
            }

            baseline.Merge(entry);
        }

        return FromLastWriterWins(entries[0].Hash, baseline);
    }
}

public static class ContentTrackerExtensions
{
    public static async Task<Result<ContentEntry>> GetSingleLocationAsync(this IContentResolver resolver, OperationContext context, ShortHash hash)
    {
        var result = await resolver.GetLocationsAsync(context, GetLocationsRequest.SingleHash(hash, recursive: true));
        return result.Select(v => v.Results.First());
    }
}
