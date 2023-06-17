// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using static Grpc.Core.Metadata;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// Entry in <see cref="LocalContentTracker"/> that holds information about a specific content hash.
/// </summary>
public class LastWriterWinsContentEntry
{
    public required long Size { get; init; }

    public DateTime CreationTime { get; private set; } = DateTime.MaxValue;

    public DateTime LastAccessTime { get; private set; } = DateTime.MinValue;
    
    /// <summary>
    /// The least upper bound of operations that have been applied to this entry. Can be used to determine the set of
    /// MachineId that currently have the piece of content. Since it contains timestamp information, it can also be
    /// used to determine if information about that specific machine is stale.
    /// </summary>
    public LastWriterWinsSet<MachineId> Operations { get; } = new();

    /// <summary>
    /// Incorporate knowledge from a single operation into this entry.
    /// </summary>
    public void Merge(Stamped<MachineId> operation)
    {
        CreationTime = CreationTime.Min(operation.ChangeStamp.TimestampUtc);
        LastAccessTime = LastAccessTime.Max(operation.ChangeStamp.TimestampUtc);

        Operations.Merge(operation);
    }

    /// <summary>
    /// Incorporate knowledge from multiple operations into this entry.
    /// </summary>
    public void MergeMany(IEnumerable<Stamped<MachineId>> operations)
    {
        // TODO: if there are many, sort them and use a single merge operation (as in merge sort). We should also
        // enforce that anything that makes it into this method is pre-sorted to avoid the cost of sorting in the first
        // place.
        foreach (var operation in operations)
        {
            Merge(operation);
        }
    }
}

/// <inheritdoc cref="ILocalContentTracker"/>
public class LocalContentTracker : StartupShutdownComponentBase, ILocalContentTracker
{
    /// <inheritdoc />
    protected override Tracer Tracer { get; } = new(nameof(LocalContentTracker));

    // NOTE: We're using a ConcurrentDictionary here, but could use a ConcurrentBigMap if we wanted to. The reason
    // we're using this data structure is that it's more common and we want to avoid premature optimization.
    private readonly ConcurrentDictionary<ShortHash, LastWriterWinsContentEntry> _content = new();

    /// <inheritdoc />
    public Task<BoolResult> UpdateLocationsAsync(OperationContext context, UpdateLocationsRequest request)
    {
        foreach (var entry in request.Entries)
        {
            try
            {
                // This code is performance sensitive, and so we're using this #if here to use a function that allows us to
                // use static lambdas and avoid lambda capture.
#if NET6_0_OR_GREATER
                _content.AddOrUpdate(
                    key: entry.Hash,
                    static (hash, incoming) =>
                    {
                        var created = new LastWriterWinsContentEntry { Size = incoming.Size, };
                        created.MergeMany(incoming.Operations);
                        return created;
                    },
                    static (hash, existing, incoming) =>
                    {
                        existing.MergeMany(incoming.Operations);
                        return existing;
                    },
                    factoryArgument: entry);
#else
                _content.AddOrUpdate(
                    key: entry.Hash,
                    _ =>
                    {
                        var created = new LastWriterWinsContentEntry { Size = entry.Size, };
                        created.MergeMany(entry.Operations);
                        return created;
                    },
                    (_, existing) =>
                    {
                        existing.MergeMany(entry.Operations);
                        return existing;
                    });
#endif
            }
            catch (Exception exception)
            {
                return Task.FromResult(new BoolResult(exception, message: $"Failed processing entry {entry}"));
            }
        }

        return BoolResult.SuccessTask;
    }

    /// <inheritdoc />
    public SequenceNumber GetSequenceNumber(ShortHash hash, MachineId machineId)
    {
        if (_content.TryGetValue(hash, out var entry))
        {
            if (entry.Operations.TryGetChangeStamp(machineId, out var timestamp))
            {
                return timestamp.SequenceNumber;
            }
        }

        return SequenceNumber.MinValue;
    }

    /// <inheritdoc />
    public Task<Result<GetLocationsResponse>> GetLocationsAsync(OperationContext context, GetLocationsRequest request)
    {
        var entries = new List<ContentEntry>(capacity: request.Hashes.Count);
        foreach (var shortHash in request.Hashes)
        {
            try
            {
                if (_content.TryGetValue(shortHash, out var entry))
                {
                    entries.Add(new ContentEntry() { Hash = shortHash, Size = entry.Size, Operations = entry.Operations.Operations, });
                }
            }
            catch (Exception exception)
            {
                return Task.FromResult(Result.FromException<GetLocationsResponse>(exception, message: $"Failed processing hash {shortHash}"));
            }
        }

        return Task.FromResult(Result.Success(new GetLocationsResponse { Results = entries, }));
    }
}
