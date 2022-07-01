// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.ConfigurationHelper;
using System.IO;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// ColdStorage is a store for handling large drives.
    /// It is queried after the MultiplexedStore and before the remote cache.
    /// The data is saved during eviction process of the <see cref="FileSystemContentStoreInternal"/>.
    /// </summary>
    public class ColdStorage : StartupShutdownSlimBase, IColdStorage
    {
        private struct RingNode
        {
            public long NodeId;
            public MachineLocation MachineLocation;

            public RingNode(long nodeId, MachineLocation machineLocation)
            {
                NodeId = nodeId;
                MachineLocation = machineLocation;
            }
        }

        /// <summary>
        /// Consistent Hashing: https://en.wikipedia.org/wiki/Consistent_hashing
        /// We create a virtual ring with the active cache servers. When we need to save or search content
        /// we use the closest nodes to the content hash in the ring. This way, the only thing that each node
        /// has to know is how the ring is built.
        /// Note: The current machine is not saved in the ring
        /// </summary>
        private RingNode[] _ring;

        private readonly ReaderWriterLockSlim _ringLock = new ReaderWriterLockSlim();

        private readonly IContentStore _store;

        private IContentSession? _session;

        protected const string SessionName = "cold_storage_session";

        protected const string ErrorMsg = "ColdStorage did not finish Startup yet";

        private readonly int _copiesQuantity;

        private readonly int _maxParallelPlaces;

        protected override Tracer Tracer { get; }  = new Tracer(nameof(ColdStorage));

        /// <summary>
        /// ColdStorage is owned by multiple FileSystemContentStoreInternal instances and each of
        /// wich will start and shut it down
        /// </summary>
        public override bool AllowMultipleStartupAndShutdowns => true;

        private readonly IAbsFileSystem _fileSystem;

        private readonly AbsolutePath _rootPath;

        private readonly IContentHasher _contentHasher;

        private readonly DistributedContentCopier _copier;

        public ColdStorage(IAbsFileSystem fileSystem, ColdStorageSettings coldStorageSettings, DistributedContentCopier distributedContentCopier)
        {
            _fileSystem = fileSystem;
            _copier = distributedContentCopier;

            _rootPath = coldStorageSettings.GetAbsoulutePath();

            _store = CreateContentStoreFromColdStorageSettings(coldStorageSettings);

            HashType hashType;
            if (!Enum.TryParse<HashType>(coldStorageSettings.ConsistentHashingHashType, true, out hashType))
            {
                hashType = HashType.SHA256;
            }
            _contentHasher = HashInfoLookup.GetContentHasher(hashType);

            _copiesQuantity = coldStorageSettings.ConsistentHashingCopiesQuantity;
            _maxParallelPlaces = coldStorageSettings.MaxParallelPlaces;

            // Starts empty and is created during the first update 
            _ring = new RingNode[0]; 
        }

        private IContentStore CreateContentStoreFromColdStorageSettings(ColdStorageSettings coldStorageSettings)
        {
            if (coldStorageSettings.RocksDbEnabled)
            {
                return new RocksDbFileSystemContentStore(_fileSystem, SystemClock.Instance, _rootPath);
            }
            ConfigurationModel configurationModel = new ConfigurationModel(new ContentStoreConfiguration(new MaxSizeQuota(coldStorageSettings.CacheSizeQuotaString!)));
            ContentStoreSettings contentStoreSettings = FromColdStorageSettings(coldStorageSettings);
            return new FileSystemContentStore(_fileSystem, SystemClock.Instance, _rootPath, configurationModel, null, contentStoreSettings, null);
        }
        private static ContentStoreSettings FromColdStorageSettings(ColdStorageSettings settings)
        {
            var result = new ContentStoreSettings()
            {
                CheckFiles = settings.CheckLocalFiles,
                SelfCheckSettings = CreateSelfCheckSettings(settings),
                OverrideUnixFileAccessMode = settings.OverrideUnixFileAccessMode,
                UseRedundantPutFileShortcut = settings.UseRedundantPutFileShortcut,
                TraceFileSystemContentStoreDiagnosticMessages = settings.TraceFileSystemContentStoreDiagnosticMessages,

                SkipTouchAndLockAcquisitionWhenPinningFromHibernation = settings.UseFastHibernationPin,
            };

            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => result.SilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.SilentOperationDurationThreshold, v => DefaultTracingConfiguration.DefaultSilentOperationDurationThreshold = TimeSpan.FromSeconds(v));
            ApplyIfNotNull(settings.DefaultPendingOperationTracingIntervalInMinutes, v => DefaultTracingConfiguration.DefaultPendingOperationTracingInterval = TimeSpan.FromMinutes(v));
            ApplyIfNotNull(settings.ReserveSpaceTimeoutInMinutes, v => result.ReserveTimeout = TimeSpan.FromMinutes(v));

            ApplyIfNotNull(settings.UseAsynchronousFileStreamOptionByDefault, v => FileSystemDefaults.UseAsynchronousFileStreamOptionByDefault = v);

            ApplyIfNotNull(settings.UseHierarchicalTraceIds, v => Context.UseHierarchicalIds = v);

            return result;
        }

        private static SelfCheckSettings CreateSelfCheckSettings(ColdStorageSettings settings)
        {
            var selfCheckSettings = new SelfCheckSettings()
            {
                Epoch = settings.SelfCheckEpoch,
                StartSelfCheckInStartup = settings.StartSelfCheckAtStartup,
                Frequency = TimeSpan.FromMinutes(settings.SelfCheckFrequencyInMinutes),
            };

            ApplyIfNotNull(settings.SelfCheckProgressReportingIntervalInMinutes, minutes => selfCheckSettings.ProgressReportingInterval = TimeSpan.FromMinutes(minutes));
            ApplyIfNotNull(settings.SelfCheckDelayInMilliseconds, milliseconds => selfCheckSettings.HashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));
            ApplyIfNotNull(settings.SelfCheckDefaultHddDelayInMilliseconds, milliseconds => selfCheckSettings.DefaultHddHashAnalysisDelay = TimeSpan.FromMilliseconds(milliseconds));

            return selfCheckSettings;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _store.StartupAsync(context).ThrowIfFailure();

            if (_fileSystem.DirectoryExists(_rootPath / "temp")) {
                _fileSystem.DeleteDirectory(_rootPath / "temp", DeleteOptions.All);
            }

            await _copier.StartupAsync(context).ThrowIfFailure();

            CreateSessionResult<IContentSession> sessionResult = _store.CreateSession(context, SessionName, ImplicitPin.None).ThrowIfFailure();
            _session = sessionResult.Session!;

            return await _session.StartupAsync(context);
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            BoolResult sessionResult = _session != null ? await _session.ShutdownAsync(context) : BoolResult.Success;
            BoolResult storeResult = await _store.ShutdownAsync(context);
            BoolResult copierResult = await _copier.ShutdownAsync(context);
            return sessionResult & storeResult & copierResult;
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return Task.FromResult(new PutResult(contentHash, ErrorMsg));
            }
            return _session.PutFileAsync(context, contentHash, path, realizationMode, cts, urgencyHint);
        }

        public Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            DisposableFile disposableFile,
            CancellationToken cts)
        {
                return PutFileAsync(context, contentHash, disposableFile.Path, FileRealizationMode.Copy, cts, UrgencyHint.Low).ContinueWith(p =>
                {
                    disposableFile.Dispose();
                    if (p.Result.Succeeded)
                    {
                        PushContentToRemoteLocations(context, p.Result, cts);
                    }
                    return p.Result;
                });
        }

        public void PushContentToRemoteLocations(
            Context context,
            PutResult putResult,
            CancellationToken cts)
        {
            var contentHashWithSize = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
            var operationContext = new OperationContext(context, cts);

            var machineLocations = GetMachineLocations(putResult.ContentHash);
            foreach (MachineLocation machine in machineLocations)
            {
                PushFileToRemoteLocationAsync(operationContext, contentHashWithSize, machine).FireAndForget(operationContext);
            }
        }

        private async Task PushFileToRemoteLocationAsync(
            OperationContext operationContext,
            ContentHashWithSize contentHashWithSize,
            MachineLocation machine)
        {
            var streamResult = await _session!.OpenStreamAsync(operationContext, contentHashWithSize.Hash, CancellationToken.None);

            // If the OpenStream fails, we do not copy the file
            if (streamResult.Succeeded)
            {
                using (streamResult.Stream)
                {
                    await _copier.PushFileAsync(
                        operationContext,
                        contentHashWithSize,
                        machine,
                        streamResult.Stream,
                        isInsideRing: false,
                        CopyReason.ColdStorage,
                        ProactiveCopyLocationSource.DesignatedLocation,
                        attempt: 1).IgnoreFailure();
                }
            }
        }

        public Task<OpenStreamResult> OpenStreamAsync(
            Context context,
            ContentHash contentHash,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return Task.FromResult(new OpenStreamResult(OpenStreamResult.ResultCode.ContentNotFound, ErrorMsg));
            }
            return _session.OpenStreamAsync(context, contentHash, cts, urgencyHint);
        }

        public Task<PutResult> PutStream(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken token,
            UrgencyHint urgencyHint)
        {
            if (_session == null) 
            {
                return Task.FromResult(new PutResult(contentHash, ErrorMsg));
            }
            return _session.PutStreamAsync(context, contentHash, stream, token, urgencyHint);
        }

        public async Task<PlaceFileResult> PlaceFileAsync(
                   Context context,
                   ContentHash contentHash,
                   AbsolutePath path,
                   FileAccessMode accessMode,
                   FileReplacementMode replacementMode,
                   FileRealizationMode realizationMode,
                   CancellationToken cts,
                   UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (_session == null)
            {
                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, ErrorMsg);
            }

            OpenStreamResult openStreamResult = await OpenStreamAsync(context, contentHash, cts);
            if (!openStreamResult.Succeeded)
            {
                // return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound, ErrorMsg)
                return new PlaceFileResult(openStreamResult);
            }

            using (var fileStream = _fileSystem.Open(path, FileAccess.ReadWrite, FileMode.OpenOrCreate, FileShare.None))
            {
                openStreamResult!.Stream.Seek(0, SeekOrigin.Begin);
                await openStreamResult.Stream.CopyToAsync(fileStream);
            }

            return PlaceFileResult.CreateSuccess(PlaceFileResult.ResultCode.PlacedWithCopy, openStreamResult.Stream.Length, source: PlaceFileResult.Source.ColdStorage);          
        }

        public async Task<PlaceFileResult> CreateTempAndPutAsync(
               OperationContext context,
               ContentHash contentHash,
               IContentSession contentSession)
        {
            OpenStreamResult openStreamResult = await OpenStreamAsync(context, contentHash, context.Token);     
            if (!openStreamResult.Succeeded)
            {
                return new PlaceFileResult(openStreamResult);
            }

            PutResult putStreamResult = await contentSession.PutStreamAsync(context, contentHash, openStreamResult.Stream, context.Token, UrgencyHint.Nominal);
            if (!putStreamResult.Succeeded)
            {
                return new PlaceFileResult(putStreamResult);
            }
            return PlaceFileResult.CreateSuccess(PlaceFileResult.ResultCode.PlacedWithCopy, putStreamResult.ContentSize, source: PlaceFileResult.Source.ColdStorage);
        }

        public async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> FetchThenPutBulkAsync(OperationContext context, IReadOnlyList<ContentHashWithPath> args, IContentSession contentSession)
        {
            var putFilesBlock =
                new TransformBlock<Indexed<ContentHashWithPath>, Indexed<PlaceFileResult>>(
                    async indexed =>
                    {
                        return new Indexed<PlaceFileResult>(await CreateTempAndPutAsync(context, indexed.Item.Hash, contentSession), indexed.Index);
                    },
                    new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = _maxParallelPlaces });

            putFilesBlock.PostAll(args.AsIndexed());

            var copyFilesLocally =
                    await Task.WhenAll(
                        Enumerable.Range(0, args.Count).Select(i => putFilesBlock.ReceiveAsync(context.Token)));
            putFilesBlock.Complete();

            return copyFilesLocally.AsTasks();
        }

        public GetBulkLocationsResult GetBulkLocations(OperationContext context, IReadOnlyList<ContentHashWithPath> contentHashes)
        {
            return context.PerformOperation(Tracer,
                operation: () =>
                {
                    var contentHashesInfo = new List<ContentHashWithSizeAndLocations>();

                    foreach (ContentHashWithPath contentHash in contentHashes)
                    {
                        var locations = GetMachineLocations(contentHash.Hash);
                        var contentHashWithLocations = new ContentHashWithSizeAndLocations(contentHash.Hash, -1, locations);

                        contentHashesInfo.Add(contentHashWithLocations);
                    }

                    return new GetBulkLocationsResult(contentHashesInfo, GetBulkOrigin.ColdStorage);
                });
        }

        private List<MachineLocation> GetMachineLocations(ContentHash contentHash)
        {
            List<MachineLocation> machines = new List<MachineLocation>();

            long contentId = contentHash.LeastSignificantLong();

            using (_ringLock.AcquireReadLock())
            {
                int firstNodeIndex = FindFirstNodeIndex(contentId);
                for (int i = 0; i < Math.Min(_ring.Length, _copiesQuantity); i++)
                {
                    int currentPosition = (firstNodeIndex + i) % _ring.Length;
                    // This implementation of Consistent Hashing does not use virtual nodes
                    machines.Add(_ring[currentPosition].MachineLocation);
                }
            };

            return machines;
        }

        private int FindFirstNodeIndex(long id)
        {
            int indx = 0;
            while (indx < _ring.Length && _ring[indx].NodeId < id)
            {
                indx++;
            }
            // Close the ring at the end
            if (indx == _ring.Length)
            {
                indx = 0;
            }
            return indx;
        }

        private RingNode[] CreateNewRing(ClusterState clusterState) {
            MachineId currentMachineId = clusterState.PrimaryMachineId;

            SortedList<long, RingNode> sortedMachines = new SortedList<long, RingNode>();

            MachineId machineId;
            foreach (var machine in clusterState.Locations)
            {
                // We use the machineId to place it in the virtual ring
                bool success = clusterState.TryResolveMachineId(machine, out machineId);

                // Create the consistent-hashing virtual ring with the active servers
                if (success && !clusterState.IsMachineMarkedInactive(machineId) && currentMachineId != machineId)
                {
                    long id = _contentHasher.GetContentHash(
                        BitConverter.GetBytes(machineId.Index)
                        ).LeastSignificantLong();
                    sortedMachines.Add(id, new RingNode(id, machine));
                }
            }

            return sortedMachines.Values.ToArray();
        }

        public Task<BoolResult> UpdateRingAsync(OperationContext context, ClusterState clusterState)
        {
            // This is a CPU intensive operation and is called from the Heartbeat so we don't want to consume the calling thread
            return context.PerformOperationAsync(Tracer,
                () => Task.Run(() =>
                    {
                        RingNode[] newRing = CreateNewRing(clusterState);

                        using (_ringLock.AcquireWriteLock())
                        {
                            _ring = newRing;
                        };
                        return BoolResult.Success;
                    })
                );
        }
    }
}
