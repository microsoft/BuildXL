// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core;
using ContentStore.Grpc;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.Ephemeral;

public class EphemeralContentSession : ContentSessionBase
{
    protected override Tracer Tracer { get; } = new(nameof(EphemeralContentSession));

    private readonly IContentSession _local;
    private readonly IContentSession _persistent;

    private readonly EphemeralHost _ephemeralHost;
    private readonly IDistributedContentCopierHost2 _contentCopierAdapter;

    /// <summary>
    /// This is a dummy implementation of the interface required by <see cref="DistributedContentCopier"/>.
    ///
    /// We don't use it because it's unnecessary.
    /// </summary>
    private class DistributedContentCopierAdapter : IDistributedContentCopierHost2
    {
        public required AbsolutePath WorkingFolder { get; init; }

        public void ReportReputation(Context context, MachineLocation location, MachineReputation reputation)
        {
        }

        public string ReportCopyResult(OperationContext context, ContentLocation info, CopyFileResult result)
        {
            return string.Empty;
        }
    }

    // TODO: when we confirm existence (or lack of) of content in the persistent session, it'd be ideal to add that
    // fact to the ephemeral cache as a "permanent fact". This would allow us to avoid the existence check in the
    // future.

    public EphemeralContentSession(string name, IContentSession local, IContentSession persistent, EphemeralHost ephemeralHost)
        : base(name, counterTracker: null)
    {
        _local = local;
        _persistent = persistent;
        _ephemeralHost = ephemeralHost;
        _contentCopierAdapter = new DistributedContentCopierAdapter { WorkingFolder = _ephemeralHost.Configuration.Workspace };
    }

    protected override async Task<PinResult> PinCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        var elision = await CheckPersistentExistanceAsync(context, contentHash, localOnly: false);
        if (elision.Allow)
        {
            return new PinResult(contentSize: elision.Size, lastAccessTime: elision.LatestPersistentTouchTime, code: PinResult.ResultCode.Success);
        }

        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return await _persistent.PinAsync(context, contentHash, context.Token, urgencyHint);
    }

    protected override async Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        var local = await _local.OpenStreamAsync(context, contentHash, context.Token, urgencyHint);
        if (local.Succeeded)
        {
            return local;
        }

        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(contentHash, context.Token);

        // Some other thread may have been downloading and inserting into the local cache. In such a case, we'll have
        // blocked above, and we can just return the result of the local cache.
        if (!guard.WaitFree)
        {
            local = await _local.OpenStreamAsync(context, contentHash, context.Token, urgencyHint);
            if (local.Succeeded)
            {
                return local;
            }
        }

        var putResult = await TryPeerToPeerFetchAsync(context, contentHash, urgencyHint);
        if (putResult.Succeeded)
        {
            local = await _local.OpenStreamAsync(context, contentHash, context.Token, urgencyHint);
            if (local.Succeeded)
            {
                return local;
            }
        }

        var session = _local as ITrustedContentSession;
        Contract.AssertNotNull(session, "The local content session was expected to be a trusted session, but failed to cast.");

        var tempLocation = AbsolutePath.CreateRandomFileName(_ephemeralHost.Configuration.Workspace);
        var persistent = await _persistent.PlaceFileAsync(
            context,
            contentHash,
            tempLocation,
            FileAccessMode.ReadOnly,
            FileReplacementMode.FailIfExists,
            FileRealizationMode.Any,
            context.Token,
            urgencyHint).ThrowIfFailureAsync();

        await session.PutTrustedFileAsync(
            context,
            new ContentHashWithSize(contentHash, persistent.FileSize),
            tempLocation,
            FileRealizationMode.Any,
            context.Token,
            urgencyHint).IgnoreFailure();

        var stream = _ephemeralHost.FileSystem.TryOpen(
            tempLocation,
            FileAccess.Read,
            FileMode.Open,
            FileShare.Delete);

        return new OpenStreamResult(stream);
    }

    protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        Contract.Requires(realizationMode != FileRealizationMode.Move, $"{nameof(EphemeralContentSession)} doesn't support {nameof(PlaceFileCoreAsync)} with {nameof(FileRealizationMode)} = {FileRealizationMode.Move}");

        var local = await _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, context.Token, urgencyHint);
        if (local.Succeeded)
        {
            return local.WithMaterializationSource(PlaceFileResult.Source.LocalCache);
        }

        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(contentHash, context.Token);

        // Some other thread may have been downloading and inserting into the local cache. In such a case, we'll have
        // blocked above, and we can just return the result of the local cache.
        if (!guard.WaitFree)
        {
            local = await _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, context.Token, urgencyHint);
            if (local.Succeeded)
            {
                return local.WithMaterializationSource(PlaceFileResult.Source.LocalCache);
            }
        }

        var datacenter = await TryPeerToPeerFetchAsync(
            context,
            contentHash,
            urgencyHint);
        if (datacenter.Succeeded)
        {
            local = await _local.PlaceFileAsync(
                context,
                contentHash,
                path,
                accessMode,
                replacementMode,
                realizationMode,
                context.Token,
                urgencyHint);
            if (local.Succeeded)
            {
                return local.WithMaterializationSource(PlaceFileResult.Source.DatacenterCache);
            }

            return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` inserted into local cache, but couldn't place from local");
        }

        var persistent = await _persistent.PlaceFileAsync(
            context,
            contentHash,
            path,
            accessMode,
            replacementMode,
            realizationMode,
            context.Token,
            urgencyHint);
        if (persistent.Succeeded)
        {
            // We insert into the local cache synchronously. This is required right now because OpenStream can delete
            // the file too early if we do it asynchronously.
            // TODO: figure out a way to do it asynchronously.
            //
            // WARNING: the adjustment in the realization mode here is important. When running QuickBuild without
            // hardlinks in cache, we need to ensure that the file is copied into the local cache when the realization
            // mode is Copy. Otherwise, the local cache will replace the incoming path with a hardlink, which will
            // cause access denied exceptions for any target that overwrites outputs from a previous target.
            var putRealizationMode = realizationMode switch
            {
                FileRealizationMode.Any => FileRealizationMode.Any,
                FileRealizationMode.Copy => FileRealizationMode.Copy,
                FileRealizationMode.HardLink => FileRealizationMode.Any,
                FileRealizationMode.CopyNoVerify => FileRealizationMode.Copy,
                _ => throw new ArgumentOutOfRangeException(nameof(realizationMode), realizationMode, null)
            };
            await _local.PutFileAsync(context, contentHash, path, putRealizationMode, context.Token, urgencyHint).IgnoreFailure();
        }

        return persistent.WithMaterializationSource(PlaceFileResult.Source.BackingStore);
    }

    private Task<PutResult> TryPeerToPeerFetchAsync(
        OperationContext context,
        ContentHash contentHash,
        UrgencyHint urgencyHint)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var locations = await _ephemeralHost.ContentResolver.GetLocationsAsync(
                    context,
                    GetLocationsRequest.SingleHash(contentHash, recursive: true));
                if (locations.Succeeded && locations.Value.Results.Count > 0)
                {
                    // We're requesting a single hash, so we need to look only at that one request.
                    var contentEntry = locations.Value.Results[0];

                    var active = new List<MachineLocation>(capacity: contentEntry.Operations.Count);
                    var inactive = new List<MachineLocation>();
                    var invalid = new List<MachineId>();
                    foreach (var machineId in contentEntry.Existing())
                    {
                        if (_ephemeralHost.ClusterStateManager.ClusterState.QueryableClusterState.RecordsByMachineId.TryGetValue(machineId, out var record))
                        {
                            // Persistent records are not eligible for peer-to-peer fetches, they are assumed to be
                            // outside of the cluster. These are, for example, Azure Blob Storage accounts.
                            if (record.Persistent)
                            {
                                continue;
                            }

                            if (record.IsInactive())
                            {
                                inactive.Add(record.Location);
                            }
                            else
                            {
                                active.Add(record.Location);
                            }
                        }
                        else
                        {
                            invalid.Add(machineId);
                        }
                    }
                    // TODO: sort so open machines wind up at the end

                    if (invalid.Count > 0)
                    {
                        Tracer.Warning(context, $"Found {invalid.Count} invalid machine IDs for content {contentHash}: {string.Join(", ", invalid)}");
                    }

                    // TODO: this could write the file down directly into the final destination, and then do an async putfile. The putfile should be fast to complete
                    if (active.Count > 0)
                    {
                        var contentHashWithSizeAndLocations = new ContentHashWithSizeAndLocations(
                            contentHash,
                            contentEntry.Size,
                            active,
                            filteredOutLocations: inactive,
                            origin: GetBulkOrigin.Local);
                        var datacenter = await _ephemeralHost.ContentCopier.TryCopyAndPutAsync(
                            context,
                            new DistributedContentCopier.CopyRequest(
                                _contentCopierAdapter,
                                contentHashWithSizeAndLocations,
                                CopyReason.Place,
                                copyInfo =>
                                {
                                    var (copyResult, tempLocation, attemptCount) = copyInfo;
                                    var local = _local as ITrustedContentSession;
                                    Contract.AssertNotNull(local, "The local content session was expected to be a trusted session, but failed to cast.");
                                    return local.PutTrustedFileAsync(context, new ContentHashWithSize(contentHash, contentEntry.Size), tempLocation, FileRealizationMode.Move, context.Token, urgencyHint);
                                },
                                CopyCompression.None,
                                null,
                                _ephemeralHost.Configuration.Workspace));

                        return datacenter;
                    }

                    return new PutResult(contentHash, $"Content hash `{contentHash}` found in the content tracker, but without any active locations");
                }

                return new PutResult(contentHash, errorMessage: $"Content hash `{contentHash}` not found in the content tracker");

            },
            extraStartMessage: $"({contentHash.ToShortString()})",
            traceOperationStarted: TraceOperationStarted,
            extraEndMessage: result =>
                             {
                                 var message = $"({contentHash.ToShortString()})";
                                 if (result.MetaData == null)
                                 {
                                     return message;
                                 }

                                 return message + $" Gate.OccupiedCount={result.MetaData.GateOccupiedCount} Gate.Wait={result.MetaData.GateWaitTime.TotalMilliseconds}ms";
                             });
    }

    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        HashType hashType,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // We can't move into the persistent store. No one should be doing this anyways, so it's fine to assert that.
        Contract.Requires(realizationMode != FileRealizationMode.Move, $"{nameof(EphemeralContentSession)} doesn't support {nameof(PutFileCoreAsync)} with {nameof(FileRealizationMode)} = {FileRealizationMode.Move}");

        var local = await _local.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
        if (IsUnrecoverablePutFailure(local))
        {
            return local;
        }

        var elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);
        if (!guard.WaitFree)
        {
            elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
            if (elision.Allow)
            {
                return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
            }
        }

        elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: false);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        return await _persistent.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
    }

    protected override async Task<PutResult> PutFileCoreAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint,
        Counter retryCounter)
    {
        // We can't move into the persistent store. No one should be doing this anyways, so it's fine to assert that.
        Contract.Requires(realizationMode != FileRealizationMode.Move, $"{nameof(EphemeralContentSession)} doesn't support {nameof(PutFileCoreAsync)} with {nameof(FileRealizationMode)} = {FileRealizationMode.Move}");

        var elision = await CheckPersistentExistanceAsync(context, contentHash, localOnly: true);
        if (elision.Allow)
        {
            return new PutResult(contentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        var local = await _local.PutFileAsync(context, contentHash, path, realizationMode, context.Token, urgencyHint);
        if (IsUnrecoverablePutFailure(local))
        {
            return local;
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);
        if (!guard.WaitFree)
        {
            elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
            if (elision.Allow)
            {
                return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
            }
        }

        elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: false);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        return await _persistent.PutFileAsync(context, local.ContentHash, path, realizationMode, context.Token, urgencyHint);
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        var position = stream.Position;
        var local = await _local.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint);
        if (IsUnrecoverablePutFailure(local))
        {
            return local;
        }

        var elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);
        if (!guard.WaitFree)
        {
            elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
            if (elision.Allow)
            {
                return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
            }
        }

        elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: false);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        stream.Position = position;
        return await _persistent.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint);
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        var elision = await CheckPersistentExistanceAsync(context, contentHash, localOnly: true);
        if (elision.Allow)
        {
            return new PutResult(contentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        var position = stream.Position;
        var local = await _local.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint);
        if (IsUnrecoverablePutFailure(local))
        {
            return local;
        }

        // Prevents duplicate PutFileAsync calls from uploading the same content at the same time. More importantly,
        // it deduplicates requests about the existence of content.
        using var guard = await _ephemeralHost.RemoteFetchLocks.AcquireAsync(local.ContentHash, context.Token);
        if (!guard.WaitFree)
        {
            elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: true);
            if (elision.Allow)
            {
                return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
            }
        }

        elision = await CheckPersistentExistanceAsync(context, local.ContentHash, localOnly: false);
        if (elision.Allow)
        {
            return new PutResult(local.ContentHash, elision.Size, contentAlreadyExistsInCache: true);
        }

        stream.Position = position;
        return await _persistent.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint);
    }


    /// <summary>
    /// This function gets called when any Put operation into the local content store fails. It determines whether we
    /// can recover from the failure to Put into the local or not. If we can't, then we return the failure as is.
    /// </summary>
    private bool IsUnrecoverablePutFailure(PutResult local)
    {
        if (local.Succeeded)
        {
            return false;
        }

        if (local.IsCancelled)
        {
            return true;
        }

        // This is absolutely horrible, but there's no error classification happening upstream.
        if (local.ErrorMessage!.Contains("and did not match expected value of"))
        {
            return true;
        }

        if (local.ErrorMessage!.Contains("The process cannot access the file because it is being used by another process"))
        {
            return true;
        }

        return false;
    }

    private readonly record struct ElisionResult(bool Allow, long Size, DateTime? LatestPersistentTouchTime);

    /// <summary>
    /// This checks for file existence in the persistent cache. This method is used to decide whether we can avoid
    /// contacting the persistent cache (i.e., save API calls).
    /// </summary>
    /// <param name="localOnly">
    /// Setting this to true means only the local content tracker will be checked. When false, the distributed content
    /// tracker will also be looked at, which involves a gRPC call to one or more hosts.
    /// </param>
    private async Task<ElisionResult> CheckPersistentExistanceAsync(OperationContext context, ShortHash contentHash, bool localOnly)
    {
        // TODO: add timeout here.
        return (await context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var now = _ephemeralHost.Clock.UtcNow;
                var local = await _ephemeralHost.LocalContentTracker.GetSingleLocationAsync(context, contentHash).ThrowIfFailureAsync();
                if (shouldElide(local, now, out var latestPersistentTouchTime))
                {
                    return Result.Success(new ElisionResult(Allow: true, Size: local.Size, LatestPersistentTouchTime: latestPersistentTouchTime));
                }

                if (localOnly)
                {
                    return Result.Success(new ElisionResult(Allow: false, Size: -1, LatestPersistentTouchTime: null));
                }

                var remote = await _ephemeralHost.ContentResolver.GetSingleLocationAsync(context, contentHash).ThrowIfFailureAsync();
                if (shouldElide(remote, now, out latestPersistentTouchTime))
                {
                    return Result.Success(new ElisionResult(Allow: true, Size: remote.Size, LatestPersistentTouchTime: latestPersistentTouchTime));
                }

                return Result.Success(new ElisionResult(Allow: false, Size: -1, LatestPersistentTouchTime: null));
            },
            traceOperationStarted: false,
            traceErrorsOnly: true,
            extraEndMessage: result =>
                             {
                                 if (result.Succeeded)
                                 {
                                     return $"ContentHash=[{contentHash}] LocalOnly=[{localOnly}] Allow=[{result.Value.Allow}] Size=[{result.Value.Size}]";
                                 }
                                 return $"ContentHash=[{contentHash}] LocalOnly=[{localOnly}] Allow=[false] Size=[-1]";
                             })).GetValueOrDefault(defaultValue: new ElisionResult(Allow: false, Size: -1, LatestPersistentTouchTime: null));

        bool shouldElide(ContentEntry contentEntry, DateTime nowUtc, out DateTime? latestPersistentTouchTime)
        {
            latestPersistentTouchTime = null;
            foreach (var operation in contentEntry.Operations)
            {
                if (operation.ChangeStamp.Operation != ChangeStampOperation.Add)
                {
                    continue;
                }

                if (operation.Value == _ephemeralHost.ClusterStateManager.ClusterState.PrimaryMachineId)
                {
                    continue;
                }

                if (!_ephemeralHost.ClusterStateManager.ClusterState.QueryableClusterState.RecordsByMachineId.TryGetValue(
                        operation.Value,
                        out var record))
                {
                    // If we couldn't resolve the machine ID, then it simply doesn't exist...
                    continue;
                }

                if (record.IsInactive())
                {
                    continue;
                }

                if (record.Persistent)
                {
                    latestPersistentTouchTime = operation.ChangeStamp.TimestampUtc.Max(latestPersistentTouchTime ?? DateTime.MinValue);
                    continue;
                }
            }

            if (latestPersistentTouchTime is not null)
            {
                // We allow non-positive values (i.e., nowUtc <= latestPersistentTouchTime) because the clock on the
                // machine may be behind w.r.t. others, or the requests may race with eachother at different timestamps
                // even within the same machine.
                return nowUtc - latestPersistentTouchTime <= _ephemeralHost.Configuration.PersistentElisionMaximumStaleness;
            }

            return false;
        }
    }

}
