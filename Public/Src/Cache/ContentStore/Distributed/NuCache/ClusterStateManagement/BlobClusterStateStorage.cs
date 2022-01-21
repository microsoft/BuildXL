// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    public class BlobClusterStateStorageConfiguration
    {
        public AzureBlobStorageCredentials? Credentials { get; set; }

        public string ContainerName { get; set; } = "checkpoints";

        public string FolderName { get; set; } = "clusterState";

        public string FileName { get; set; } = "clusterState.json";

        public TimeSpan StorageInteractionTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public bool Standalone { get; set; } = false;

        public ClusterStateRecomputeConfiguration RecomputeConfiguration { get; set; } = new ClusterStateRecomputeConfiguration();
    }

    public class BlobClusterStateStorage : StartupShutdownSlimBase, IClusterStateStorage, ISecondaryClusterStateStorage
    {
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobClusterStateStorage));

        private readonly BlobClusterStateStorageConfiguration _configuration;
        private readonly IClock _clock;

        private readonly CloudBlobClient _client;
        private readonly CloudBlobContainer _container;
        private readonly CloudBlobDirectory _directory;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        public BlobClusterStateStorage(
            BlobClusterStateStorageConfiguration configuration,
            IClock? clock = null)
        {
            Contract.RequiresNotNull(configuration.Credentials);
            _configuration = configuration;
            _clock = clock ?? SystemClock.Instance;

            _client = _configuration.Credentials!.CreateCloudBlobClient();
            _container = _client.GetContainerReference(_configuration.ContainerName);
            _directory = _container.GetDirectoryReference(_configuration.FolderName);
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _container.CreateIfNotExistsAsync(
                accessType: BlobContainerPublicAccessType.Off,
                options: DefaultBlobStorageRequestOptions,
                operationContext: null,
                cancellationToken: context.Token);

            return await base.StartupCoreAsync(context);
        }

        private record State(string? ETag = null, ClusterStateMachine? Value = null);

        private Task<Result<(ClusterStateMachine Next, TReturn RedisValue)>> ReadModifyWriteAsync<TState, TReturn>(OperationContext context, Func<TState, ClusterStateMachine, (ClusterStateMachine Next, TReturn ReturnValue)> transform, TState state)
        {
            return context.PerformOperationAsync(Tracer,
                async () =>
                {
                    while (true)
                    {
                        context.Token.ThrowIfCancellationRequested();

                        // TODO?: It is possible that caching the last state may allow us to avoid doing some reads by
                        // pre-emptively trying to update on the basis of the old values. I am not sure if we should
                        // expect this optimization to be helpful in actual prod environments.
                        State currentState = await ReadStateAsync(context).ThrowIfFailureAsync();
                        var currentValue = currentState.Value ?? new ClusterStateMachine();
                        var next = transform(state, currentValue);
                        if (ReferenceEquals(currentValue, next))
                        {
                            return Result.Success(next);
                        }

                        var succeeded = await CompareUpdateStateAsync(context, next.Next, currentState.ETag).ThrowIfFailureAsync();
                        if (succeeded)
                        {
                            return Result.Success(next);
                        }
                    }
                },
                traceOperationStarted: false);
        }

        private Task<Result<State>> ReadStateAsync(OperationContext context)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var blob = _directory.GetBlockBlobReference(_configuration.FileName);

                    var downloadContext = new Microsoft.WindowsAzure.Storage.OperationContext();
                    string jsonText;
                    try
                    {
                        jsonText = await blob.DownloadTextAsync(
                            operationContext: downloadContext,
                            cancellationToken: context.Token,
                            encoding: Encoding.UTF8,
                            accessCondition: null,
                            options: DefaultBlobStorageRequestOptions);
                    }
                    catch (StorageException e) when (e.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
                    {
                        return Result.Success(new State());
                    }

                    var value = JsonUtilities.JsonDeserialize<ClusterStateMachine>(jsonText);
                    return Result.Success(new State(downloadContext.LastResult.Etag, value));
                },
                extraEndMessage: (Func<Result<State>, string>?)(r =>
                {
                    if (!r.Succeeded)
                    {
                        return string.Empty;
                    }

                    // We do not log the cluster state here because the file is too large and would spam the logs
                    var value = r.Value;
                    return $"ETag=[{value?.ETag ?? "null"}]";
                }),
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<Result<bool>> CompareUpdateStateAsync(
            OperationContext context,
            ClusterStateMachine value,
            string? etag)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async (context) =>
                {
                    var jsonText = JsonUtilities.JsonSerialize(value, indent: true);

                    var reference = _directory.GetBlockBlobReference(_configuration.FileName);
                    var accessCondition =
                        etag is null ?
                            AccessCondition.GenerateIfNotExistsCondition() :
                            AccessCondition.GenerateIfMatchCondition(etag);
                    try
                    {
                        await reference.UploadTextAsync(
                            jsonText,
                            Encoding.UTF8,
                            accessCondition: accessCondition,
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // We obtain PreconditionFailed when If-Match fails, and NotModified when If-None-Match fails
                        // (corresponds to IfNotExistsCondition)
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed
                            || exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotModified
                            // Used only in the development storage case
                            || exception.RequestInformation.ErrorCode == "BlobAlreadyExists")
                        {
                            Tracer.Debug(
                                context,
                                exception,
                                $"Value does not exist or does not match ETag `{etag ?? "null"}`. Reported ETag is `{exception.RequestInformation.Etag ?? "null"}`");
                            return Result.Success(false);
                        }

                        throw;
                    }

                    // Uploaded successfully, so we overwrote the previous value
                    return Result.Success(true);
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    // We do not log the cluster state here because the file is too large and would spam the logs
                    var msg = $"ETag=[{etag ?? "null"}]";
                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Exchanged=[{r.Value}]";
                },
                timeout: _configuration.StorageInteractionTimeout);
        }

        public Task<Result<MachineMapping>> RegisterMachineAsync(OperationContext context, MachineLocation machineLocation)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var (_, assignedMachineId) = await ReadModifyWriteAsync(context, static (arguments, current) =>
                {
                    var next = current;
                    if (!current.TryResolveMachineId(arguments.Location, out var machineId))
                    {
                        (next, machineId) = current.RegisterMachine(arguments.Location, arguments.Now);
                    }

                    next = next.Recompute(arguments.Configuration, arguments.Now);

                    return (next, machineId);
                }, (Location: machineLocation, Configuration: _configuration.RecomputeConfiguration, Now: _clock.UtcNow)).ThrowIfFailureAsync();

                return Result.Success(new MachineMapping(machineLocation, assignedMachineId));
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Mapping=[{r.ToStringOr($"(Id: Failure, Location: {machineLocation})")}]");
        }

        public Task<BoolResult> ForceRegisterMachineAsync(OperationContext context, MachineMapping mapping)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                await ReadModifyWriteAsync(context, static (arguments, current) =>
                {
                    var next = current.ForceRegisterMachine(arguments.Mapping.Id, arguments.Mapping.Location, arguments.Now);
                    next = next.Recompute(arguments.Configuration, arguments.Now);

                    return (next, Unit.Void);
                }, (Mapping: mapping, Configuration: _configuration.RecomputeConfiguration, Now: _clock.UtcNow)).ThrowIfFailureAsync();

                return BoolResult.Success;
            },
            traceOperationStarted: false,
            extraEndMessage: _ =>
            {
                return $"Mapping=[{mapping}]";
            });
        }

        public Task<Result<HeartbeatMachineResponse>> HeartbeatAsync(OperationContext context, HeartbeatMachineRequest request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var now = _clock.UtcNow;
                request.HeartbeatTime ??= now;

                var (currentState, priorStatus) = await ReadModifyWriteAsync(context, static (arguments, current) =>
                {
                    var request = arguments.Request;
                    var (next, priorStatus) = current.Heartbeat(request.MachineId, request.HeartbeatTime!.Value, request.DeclaredMachineState).ThrowIfFailure();
                    next = next.Recompute(arguments.Configuration, arguments.Now);

                    return (next, priorStatus);
                }, (Request: request, Configuration: _configuration.RecomputeConfiguration, Now: now)).ThrowIfFailureAsync();

                return Result.Success(new HeartbeatMachineResponse()
                {
                    Added = false,
                    PriorState = priorStatus.State,
                    InactiveMachines = currentState.InactiveMachinesToBitMachineIdSet(),
                    ClosedMachines = currentState.ClosedMachinesToBitMachineIdSet(),
                });
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Request=[{request}] Response=[{r.ToStringOr("Error")}]");
        }

        public Task<Result<GetClusterUpdatesResponse>> GetClusterUpdatesAsync(OperationContext context, GetClusterUpdatesRequest request)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                var clusterState = (await ReadStateAsync(context).ThrowIfFailureAsync()).Value;
                if (clusterState is null || request.MaxMachineId >= clusterState.NextMachineId)
                {
                    return Result.Success(new GetClusterUpdatesResponse()
                    {
                        UnknownMachines = new Dictionary<MachineId, MachineLocation>(),
                        MaxMachineId = Math.Max(request.MaxMachineId, (clusterState?.NextMachineId ?? 1) - 1),
                    });
                }

                return Result.Success(new GetClusterUpdatesResponse()
                {
                    UnknownMachines = clusterState.Records.Where(r => r.Id.Index > request.MaxMachineId)
                                                          .ToDictionary(r => r.Id, r => r.Location),
                    MaxMachineId = clusterState.NextMachineId - 1,
                });
            },
            traceOperationStarted: false,
            extraEndMessage: r => $"Request=[{request}] Response=[{r.ToStringOr("Error")}]");
        }
    }
}
