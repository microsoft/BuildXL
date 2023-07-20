// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
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

    public EphemeralContentSession(string name, IContentSession local, IContentSession persistent, EphemeralHost ephemeralHost)
        : base(name, counterTracker: null)
    {
        _local = local;
        _persistent = persistent;
        _ephemeralHost = ephemeralHost;
        _contentCopierAdapter = new DistributedContentCopierAdapter { WorkingFolder = _ephemeralHost.Configuration.Workspace };
    }

    protected override Task<PinResult> PinCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHash, context.Token, urgencyHint);
    }

    protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHashes, context.Token, urgencyHint);
    }

    public override Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinOperationConfiguration configuration)
    {
        // Pins are sent directly to the persistent store because the local store is expected to be too small to hold
        // the entire content of the build.
        return _persistent.PinAsync(context, contentHashes, configuration);
    }

    protected override async Task<OpenStreamResult> OpenStreamCoreAsync(OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
    {
        var local = await _local.OpenStreamAsync(context, contentHash, context.Token, urgencyHint);
        if (local.Succeeded)
        {
            return local;
        }

        // The following logic relies on the fact that we can create a file stream pointing to a file, emit a delete,
        // and the file will be deleted when the last remaining file handle is closed.
        using var temporary = new DisposableFile(context, _ephemeralHost.FileSystem, AbsolutePath.CreateRandomFileName(_ephemeralHost.Configuration.Workspace));

        var placeResult = await PlaceFileCoreAsync(
            context,
            contentHash,
            temporary.Path,
            FileAccessMode.ReadOnly,
            FileReplacementMode.ReplaceExisting,
            FileRealizationMode.Any,
            urgencyHint,
            retryCounter);
        if (!placeResult.Succeeded)
        {
            if (placeResult.Code == PlaceFileResult.ResultCode.NotPlacedContentNotFound)
            {
                return new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, errorMessage: $"Content with hash {contentHash} was not found");
            }

            return new OpenStreamResult(placeResult, message: $"Failed to find content with hash {contentHash}");
        }

        // We don't dispose the stream on purpose, because the callee takes ownership of it.
        var stream = _ephemeralHost.FileSystem.TryOpen(
            temporary.Path,
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
        // Step 1: try to fetch it from the local content store.
        var local = await _local.PlaceFileAsync(context, contentHash, path, accessMode, replacementMode, realizationMode, context.Token, urgencyHint);
        if (local.Succeeded)
        {
            return local;
        }

        // Step 2: try to fetch it from the datacenter cache.
        var datacenter = await TryPlaceFromDatacenterCacheAsync(
            context,
            contentHash,
            path,
            accessMode,
            replacementMode,
            realizationMode,
            urgencyHint);
        if (datacenter.Succeeded)
        {
            return datacenter;
        }

        // Step 3: try to fetch it from the persistent cache.
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
            // We're inserting into the local fully asynchronously here because we don't need it to succeed at all for
            // the build to succeed.
            _ = _local.PutFileAsync(context, contentHash, path, FileRealizationMode.Any, context.Token, urgencyHint).FireAndForgetErrorsAsync(context);
        }

        return persistent.WithMaterializationSource(PlaceFileResult.Source.BackingStore);
    }

    private Task<PlaceFileResult> TryPlaceFromDatacenterCacheAsync(
        OperationContext context,
        ContentHash contentHash,
        AbsolutePath path,
        FileAccessMode accessMode,
        FileReplacementMode replacementMode,
        FileRealizationMode realizationMode,
        UrgencyHint urgencyHint)
    {
        return context.PerformOperationAsync(
            Tracer,
            async () =>
            {
                var locations = await _ephemeralHost.DistributedContentTracker.GetLocationsAsync(
                    context,
                    new GetLocationsRequest() { Hashes = new[] { (ShortHash)contentHash }, });
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
                            if (record.IsOpen())
                            {
                                active.Add(record.Location);
                            }
                            else
                            {
                                inactive.Add(record.Location);
                            }
                        }
                        else
                        {
                            invalid.Add(machineId);
                        }
                    }

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
                                    return _local.PutFileAsync(context, contentHash, tempLocation, FileRealizationMode.Any, context.Token, urgencyHint);
                                },
                                CopyCompression.None,
                                null,
                                _ephemeralHost.Configuration.Workspace));

                        if (datacenter.Succeeded)
                        {
                            var local = await _local.PlaceFileAsync(
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

                        return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` couldn't be downloaded from peers");
                    }

                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` found in the content tracker, but without any active locations");
                }

                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, errorMessage: $"Content hash `{contentHash}` not found in the content tracker");

            },
            extraStartMessage: $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})",
            traceOperationStarted: TraceOperationStarted,
            extraEndMessage: result =>
                             {
                                 var message = $"({contentHash.ToShortString()},{path},{accessMode},{replacementMode},{realizationMode})";
                                 if (result.Metadata == null)
                                 {
                                     return message;
                                 }

                                 return message + $" Gate.OccupiedCount={result.Metadata.GateOccupiedCount} Gate.Wait={result.Metadata.GateWaitTime.TotalMilliseconds}ms";
                             },
            traceErrorsOnly: TraceErrorsOnlyForPlaceFile(path));
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
        var persistent = _persistent.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
        var local = _local.PutFileAsync(context, hashType, path, realizationMode, context.Token, urgencyHint);
        await TaskUtilities.SafeWhenAll(local, persistent);
        return await persistent;
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

        var persistent = _persistent.PutFileAsync(context, contentHash, path, realizationMode, context.Token, urgencyHint);
        var local = _local.PutFileAsync(context, contentHash, path, realizationMode, context.Token, urgencyHint).IgnoreFailure();
        await TaskUtilities.SafeWhenAll(local, persistent);

        // REMARK: we don't combine the errors here because we're attempting to avoid failing the build unless we
        // absolutely have to. 
        return await persistent;
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        // REMARK: there's an alternative option here where we insert into local, and then get a stream to the local
        // that we use to insert into persistent. This would allow us to avoid awaiting on the persistent before
        // returning. However, that exposes us to the following error scenario:
        //  1. Insert into local, get stream to local, create task to insert into persistent.
        //  2. Because the caller sees that the stream was inserted, it creates a fingerprint that references it.
        //  3. The task to insert into persistent fails, or times out, or takes too long to get scheduled.
        //  4. Some other build gets a cache hit off that fingerprint, tries to open the stream, and fails because the
        //     stream is not in the persistent store.

        // PutStream needs to be serialized because it's a single stream that needs to be inserted in both the local
        // and persistent stores.
        var position = stream.Position;
        var persistent = await _persistent.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint);
        if (!persistent.Succeeded)
        {
            return persistent;
        }

        stream.Position = position;

        // Local failure is ignored because it's not required for the build to complete. Also, if there were any
        // failures they must have already been logged by the time the following task completes.
        // We MUST await because the stream might be disposed before the task completes otherwise.
        await _local.PutStreamAsync(context, hashType, stream, context.Token, urgencyHint).IgnoreFailure();

        return persistent;
    }

    protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
    {
        Contract.Requires(stream.CanSeek, $"{nameof(EphemeralContentSession)} needs to be able to seek the incoming stream.");

        // REMARK: there's an alternative option here where we insert into local, and then get a stream to the local
        // that we use to insert into persistent. This would allow us to avoid awaiting on the persistent before
        // returning. However, that exposes us to the following error scenario:
        //  1. Insert into local, get stream to local, create task to insert into persistent.
        //  2. Because the caller sees that the stream was inserted, it creates a fingerprint that references it.
        //  3. The task to insert into persistent fails, or times out, or takes too long to get scheduled.
        //  4. Some other build gets a cache hit off that fingerprint, tries to open the stream, and fails because the
        //     stream is not in the persistent store.

        // PutStream needs to be serialized because it's a single stream that needs to be inserted in both the local
        // and persistent stores.
        var position = stream.Position;
        var persistent = await _persistent.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint);
        if (!persistent.Succeeded)
        {
            return persistent;
        }

        stream.Position = position;

        // Local failure is ignored because it's not required for the build to complete. Also, if there were any
        // failures they must have already been logged by the time the following task completes.
        // We MUST await because the stream might be disposed before the task completes otherwise.
        await _local.PutStreamAsync(context, contentHash, stream, context.Token, urgencyHint).IgnoreFailure();

        return persistent;
    }
}
