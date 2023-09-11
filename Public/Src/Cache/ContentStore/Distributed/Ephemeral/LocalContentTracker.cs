// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

/// <summary>
/// Entry in <see cref="LocalContentTracker"/> that holds information about a specific content hash.
/// </summary>
/// <remarks>
/// This doesn't store the <see cref="ShortHash"/> it belongs to in order to prevent memory bloat.
/// </remarks>
public class LastWriterWinsContentEntry
{
    public required long Size { get; set; }

    /// <summary>
    /// The least upper bound of operations that have been applied to this entry. Can be used to determine the set of
    /// MachineId that currently have the piece of content. Since it contains timestamp information, it can also be
    /// used to determine if information about that specific machine is stale.
    /// </summary>
    public LastWriterWinsSet<MachineId> Operations { get; } = LastWriterWinsSet<MachineId>.Empty();

    public void Merge(ContentEntry other)
    {
        Size = Math.Max(Size, other.Size);
        Operations.MergePreSorted(other.Operations);
    }

    public static LastWriterWinsContentEntry From(ContentEntry entry)
    {
        var output = new LastWriterWinsContentEntry()
        {
            Size = entry.Size
        };
        output.Merge(entry);
        return output;
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
                    static (hash, entry) => LastWriterWinsContentEntry.From(entry),
                    static (hash, existing, entry) =>
                    {
                        existing.Merge(entry);
                        return existing;
                    },
                    factoryArgument: entry);
#else
                _content.AddOrUpdate(
                    key: entry.Hash,
                    _ => LastWriterWinsContentEntry.From(entry),
                    (_, existing) =>
                    {
                        existing.Merge(entry);
                        return existing;
                    });
#endif
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, $"Failed processing entry {entry}. Skipping it instead.");
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
            bool added = false;
            try
            {
                if (_content.TryGetValue(shortHash, out var entry))
                {
                    entries.Add(ContentEntry.FromLastWriterWins(shortHash, entry));
                    added = true;
                }
            }
            catch (Exception exception)
            {
                Tracer.Error(context, exception, $"Failed processing hash {shortHash}. Returning empty response for it.");
            }
            finally
            {
                if (!added)
                {
                    entries.Add(ContentEntry.Unknown(shortHash));
                }
            }
        }

        Contract.Assert(request.Hashes.Count == entries.Count, "The number of output responses must be equal and in the same order as the input requests");
        return Task.FromResult(Result.Success(new GetLocationsResponse { Results = entries, }));
    }
}
