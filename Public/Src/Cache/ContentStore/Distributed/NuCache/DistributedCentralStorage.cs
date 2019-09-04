// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// <see cref="CentralStorage"/> which uses uses distributed CAS as cache aside for a fallback central storage
    /// </summary>
    public class DistributedCentralStorage : CentralStorage
    {
        private const string StorageIdSeparator = "||DCS||";
        private readonly DistributedCentralStoreConfiguration _configuration;
        private ILocationStore _locationStore;
        private readonly IDistributedContentCopier _copier;
        private const string CacheSubFolderName = "dcs";
        private const string CacheSubFolderNameWithTrailingSlash = CacheSubFolderName + @"\";

        private const string CacheSharedSubFolderToReplace = @"Shared\" + CacheSubFolderName;
        private const string CacheSharedSubFolder = CacheSubFolderName + @"\Shared";

        private readonly CentralStorage _fallbackStorage;
        private readonly ConcurrentDictionary<MachineLocation, MachineLocation> _machineLocationTranslationMap = new ConcurrentDictionary<MachineLocation, MachineLocation>();

        // Choosing MD5 hash type as hash type for peer to peer storage somewhat arbitrarily. However, it has the nice
        // property of not being a standard hash type used for normal CAS content so traffic can easily be differentiated
        private readonly HashType _hashType = HashType.MD5;

        // Randomly generated seed for use when computing derived hash represent fake content for tracking
        // which machines have started copying a particular piece of content
        private const uint _startedCopyHashSeed = 1006063109;
        private readonly FileSystemContentStoreInternal _privateCas;
        private int _translateLocationsOffset = 0;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedCentralStorage));

        /// <inheritdoc />
        protected override string PreprocessStorageId(string storageId) => storageId;

        /// <nodoc />
        public DistributedCentralStorage(
            DistributedCentralStoreConfiguration configuration,
            IDistributedContentCopier copier,
            CentralStorage fallbackStorage)
        {
            _configuration = configuration;
            _copier = copier;
            _fallbackStorage = fallbackStorage;

            var maxRetentionMb = configuration.MaxRetentionGb * 1024;
            var softRetentionMb = (int)(maxRetentionMb * 0.8);

            // Create a private CAS for storing checkpoint data
            // Avoid introducing churn into primary CAS
            _privateCas = new FileSystemContentStoreInternal(
                new PassThroughFileSystem(),
                SystemClock.Instance,
                configuration.CacheRoot / CacheSubFolderName,
                new ConfigurationModel(
                    new ContentStoreConfiguration(new MaxSizeQuota(hardExpression: maxRetentionMb + "MB", softExpression: softRetentionMb + "MB")),
                    ConfigurationSelection.RequireAndUseInProcessConfiguration));
        }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(OperationContext context, ILocationStore locationStore)
        {
            _locationStore = locationStore;
            return StartupAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            Contract.Assert(_locationStore != null);

            await _privateCas.StartupAsync(context).ThrowIfFailure();
            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _privateCas.ShutdownAsync(context).ThrowIfFailure();
            return await base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string storageId, bool isUploader)
        {
            var (hash, fallbackStorageId) = ParseCompositeStorageId(storageId);

            // Need to touch in fallback storage as well so it knows the content is still in use
            var touchTask = _fallbackStorage.TouchBlobAsync(context, file, fallbackStorageId, isUploader).ThrowIfFailure();

            // Ensure content is present in private CAS and registered
            var registerTask = PutAndRegisterFileAsync(context, file, hash);

            await Task.WhenAll(touchTask, registerTask);

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath)
        {
            // Get the content from peers or fallback
            var contentHashWithSize = await TryGetAndPutFileAsync(context, storageId, targetFilePath).ThrowIfFailureAsync();

            // Register that the machine now has the content
            await RegisterContent(context, contentHashWithSize);

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool garbageCollect = false)
        {
            // Add the file to CAS and register with global content location store
            var hashTask = PutAndRegisterFileAsync(context, file, hash: null);

            // Upload to fallback storage so file is available if needed from there
            var innerStorageIdTask = _fallbackStorage.UploadFileAsync(context, file, blobName, garbageCollect).ThrowIfFailureAsync();

            await Task.WhenAll(hashTask, innerStorageIdTask);

            var hash = await hashTask;
            var innerStorageId = await innerStorageIdTask;

            return CreateCompositeStorageId(hash, innerStorageId);
        }

        private async Task<Result<ContentHashWithSize>> TryGetAndPutFileAsync(OperationContext context, string storageId, AbsolutePath targetFilePath)
        {
            var (hash, fallbackStorageId) = ParseCompositeStorageId(storageId);
            if (hash != null)
            {
                // First attempt to place file from content store
                var placeResult = await _privateCas.PlaceFileAsync(context, hash.Value, targetFilePath, FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.CopyNoVerify, pinRequest: null);
                if (placeResult.IsPlaced())
                {
                    return Result.Success(new ContentHashWithSize(hash.Value, placeResult.FileSize));
                }

                // If not placed, try to copy from a peer into private CAS
                var putResult = await CopyLocalAndPutAsync(context, hash.Value);
                if (putResult.Succeeded)
                {
                    // Lastly, try to place again now that file is copied to CAS
                    placeResult = await _privateCas.PlaceFileAsync(context, hash.Value, targetFilePath, FileAccessMode.Write, FileReplacementMode.ReplaceExisting, FileRealizationMode.CopyNoVerify, pinRequest: null).ThrowIfFailure();

                    Counters[CentralStorageCounters.TryGetFileFromPeerSucceeded].Increment();
                    return Result.Success(new ContentHashWithSize(hash.Value, placeResult.FileSize));
                }
            }

            Counters[CentralStorageCounters.TryGetFileFromFallback].Increment();
            return await TryGetFromFallbackAndPutAsync(context, targetFilePath, fallbackStorageId);
        }

        private string CreateCompositeStorageId(ContentHash hash, string fallbackStorageId)
        {
            // Storage id format:
            // {Hash}{StorageIdSeparator}{fallbackStorageId}
            return string.Join(StorageIdSeparator, hash, fallbackStorageId);
        }

        internal static (ContentHash? hash, string fallbackStorageId) ParseCompositeStorageId(string storageId)
        {
            if (storageId.Contains(StorageIdSeparator))
            {
                // Storage id is a composite id. Split out parts
                var parts = storageId.Split(new[] { StorageIdSeparator }, StringSplitOptions.None);
                Contract.Assert(parts.Length == 2);
                return (hash: new ContentHash(parts[0]), fallbackStorageId: parts[1]);
            }
            else
            {
                // Storage id is not a composite id. This happens when we get ids from when the distributed central
                // storage is disabled. Just return the full storage id as the fallback storage id
                return (hash: null, fallbackStorageId: storageId);
            }
        }

        private async Task<PutResult> CopyLocalAndPutAsync(OperationContext context, ContentHash hash)
        {
            var startedCopyHash = ComputeStartedCopyHash(hash);
            await RegisterContent(context, new ContentHashWithSize(startedCopyHash, -1));

            for (int i = 0; i < _configuration.PropagationIterations; i++)
            {
                // If initial place fails, try to copy the content from remote locations
                var (hashInfo, pendingCopyCount) = await GetFileLocationsAsync(context, hash, startedCopyHash);

                var machineId = _locationStore.LocalMachineId.Index;
                int machineNumber = GetMachineNumber();
                var requiredReplicas = ComputeRequiredReplicas(machineNumber);

                var actualReplicas = hashInfo.Locations.Count;

                // Copy from peers if:
                // The number of pending copies is known to be less that the max allowed copies
                // OR the number replicas exceeds the number of required replicas computed based on the machine index
                bool shouldCopy = pendingCopyCount < _configuration.MaxSimultaneousCopies || actualReplicas >= requiredReplicas;

                Tracer.OperationDebug(context, $"{i} (ShouldCopy={shouldCopy}): Id={machineId}" +
                    $", Replicas={actualReplicas}, RequiredReplicas={requiredReplicas}, Pending={pendingCopyCount}, Max={_configuration.MaxSimultaneousCopies}");

                if (shouldCopy)
                {
                    var putResult = await _copier.TryCopyAndPutAsync(context, hashInfo,
                        args => _privateCas.PutFileAsync(context, args.tempLocation, FileRealizationMode.Move, hash, pinRequest: null));

                    return putResult;
                }

                // Wait for content to propagate to more machines
                await Task.Delay(_configuration.PropagationDelay, context.Token);
            }

            return new ErrorResult("Insufficient replicas").AsResult<PutResult>();
        }

        /// <summary>
        /// Try to get from the fallback and put in the CAS
        /// </summary>
        private async Task<ContentHashWithSize> TryGetFromFallbackAndPutAsync(OperationContext context, AbsolutePath targetFilePath, string fallbackStorageId)
        {
            // In the success case the content will be put at targetFilePath
            await _fallbackStorage.TryGetFileAsync(context, fallbackStorageId, targetFilePath).ThrowIfFailure();

            var putResult = await _privateCas.PutFileAsync(context, targetFilePath, FileRealizationMode.Copy, _hashType, pinRequest: null).ThrowIfFailure();

            return new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
        }

        private async Task<ContentHash> PutAndRegisterFileAsync(OperationContext context, AbsolutePath file, ContentHash? hash)
        {
            PutResult putResult;
            if (hash != null)
            {
                putResult = await _privateCas.PutFileAsync(context, file, FileRealizationMode.Copy, hash.Value, pinRequest: null).ThrowIfFailure();
            }
            else
            {
                putResult = await _privateCas.PutFileAsync(context, file, FileRealizationMode.Copy, _hashType, pinRequest: null).ThrowIfFailure();
            }

            var contentInfo = new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize);
            await RegisterContent(context, contentInfo);

            return putResult.ContentHash;
        }

        private async Task<(ContentHashWithSizeAndLocations info, int pendingCopies)> GetFileLocationsAsync(OperationContext context, ContentHash hash, ContentHash startedCopyHash)
        {
            // Locations are registered under the derived fake startedCopyHash to keep a count of which machines have started
            // copying content. This allows computing the amount of pending copies by subtracting the machines which have
            // finished copying (i.e. location is registered with real hash)
            var result = await _locationStore.GetBulkAsync(context, new[] { hash, startedCopyHash }).ThrowIfFailure();
            var info = result.ContentHashesInfo[0];

            var startedCopyLocations = result.ContentHashesInfo[1].Locations;
            var finishedCopyLocations = info.Locations;
            var pendingCopies = startedCopyLocations.Except(finishedCopyLocations).Count();

            return (new ContentHashWithSizeAndLocations(info.ContentHash, info.Size, TranslateLocations(info.Locations)), pendingCopies);
        }

        private ContentHash ComputeStartedCopyHash(ContentHash hash)
        {
            var murmurHash = BuildXL.Utilities.MurmurHash3.Create(hash.ToByteArray(), _startedCopyHashSeed);

            var hashLength = HashInfoLookup.Find(_hashType).ByteLength;
            var buffer = murmurHash.ToByteArray();
            Array.Resize(ref buffer, hashLength);

            return new ContentHash(_hashType, buffer);
        }

        private Task RegisterContent(OperationContext context, params ContentHashWithSize[] contentInfo)
        {
            return _locationStore.RegisterLocalLocationAsync(context, contentInfo).ThrowIfFailure();
        }

        private IReadOnlyList<MachineLocation> TranslateLocations(IReadOnlyList<MachineLocation> locations)
        {
            // Choose a 'random' offset to ensure that locations are random
            // Locations are normally randomly sorted except machine reputation can override this
            // For content which is pulled on all machines like that in the central storage, it is more
            // important not to overload a machine which may end up consistent at the top of the list because of
            // having a good reputation
            var offset = Interlocked.Increment(ref _translateLocationsOffset);
            return locations.SelectList((item, index) => TranslateLocation(locations[(offset + index) % locations.Count]));
        }

        private MachineLocation TranslateLocation(MachineLocation other)
        {
            if (_machineLocationTranslationMap.TryGetValue(other, out var translated))
            {
                return translated;
            }

            var otherPath = other.Path;

            bool hasTrailingSlash = otherPath.EndsWith(@"\");

            // Add dcs subfolder to the path
            otherPath = Path.Combine(otherPath, hasTrailingSlash ? CacheSubFolderNameWithTrailingSlash : CacheSubFolderName);

            // If other already ended with shared, this will rearrange so that the shared folder is under the dcs sub folder
            otherPath = otherPath.ReplaceIgnoreCase(CacheSharedSubFolderToReplace, CacheSharedSubFolder);

            var location = new MachineLocation(otherPath);
            _machineLocationTranslationMap[other] = location;
            return location;
        }

        /// <summary>
        /// Computes an index for the machine among active machines
        /// </summary>
        private int GetMachineNumber()
        {
            var machineId = _locationStore.LocalMachineId.Index;
            var machineNumber = machineId - _locationStore.ClusterState.InactiveMachines.Where(id => id.Index < machineId).Count();
            return machineNumber;
        }

        private int ComputeRequiredReplicas(int index)
        {
            if (index <= 0)
            {
                return 1;
            }

            // Threshold is index / MaxSimultaneousCopies.
            // This ensures when locations are chosen at random there should be on average MaxSimultaneousCopies or less
            // from the set of locations assuming worst case where all machines are trying to copy concurrently
            var machineThreshold = index / _configuration.MaxSimultaneousCopies;
            return Math.Max(1, machineThreshold);
        }

        /// <summary>
        /// Defines content location store functionality needed for <see cref="DistributedCentralStorage"/>
        /// </summary>
        public interface ILocationStore
        {
            /// <summary>
            /// The local machine id
            /// </summary>
            MachineId LocalMachineId { get; }

            /// <summary>
            /// The cluster state
            /// </summary>
            ClusterState ClusterState { get; }

            /// <summary>
            /// Gets content locations for content
            /// </summary>
            Task<GetBulkLocationsResult> GetBulkAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes);

            /// <summary>
            /// Registers content location for current machine
            /// </summary>
            Task<BoolResult> RegisterLocalLocationAsync(OperationContext context, IReadOnlyList<ContentHashWithSize> contentInfo);
        }
    }
}
