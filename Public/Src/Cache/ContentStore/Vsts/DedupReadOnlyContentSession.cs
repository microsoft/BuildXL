// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.WindowsAzure.Storage;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;
using VstsDedupIdentifier = Microsoft.VisualStudio.Services.BlobStore.Common.DedupIdentifier;
using VstsBlobIdentifier = Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier;
using System.Threading.Tasks.Dataflow;
using FileInfo = System.IO.FileInfo;
using System.Runtime.CompilerServices;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     IReadOnlyContentSession for DedupContentStore.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class DedupReadOnlyContentSession : IReadOnlyContentSession
    {
        /// <summary>
        /// Default number of oustanding connections to throttle Artifact Services.
        /// TODO: Unify cache config - current default taken from IOGate in DistributedReadOnlyContentSession.
        /// </summary>
        protected const int DefaultMaxConnections = 512;

        /// <summary>
        /// If operation waits longer than this value to get past ConnectionGate, write warning to log.
        /// </summary>
        private const int MinLogWaitTimeInSeconds = 1;

        /// <summary>
        /// Default number of tasks to process in parallel.
        /// </summary>
        private const int DefaultMaxParallelism = 16;

        /// <summary>
        ///     Required HashType for Dedup content sessions.
        /// </summary>
        protected const HashType RequiredHashType = HashType.DedupNodeOrChunk;

        /// <summary>
        ///     Size for stream buffers to temp files.
        /// </summary>
        protected const int StreamBufferSize = 16384;

        /// <summary>
        ///     Policy determining whether or not content should be automatically pinned on adds or gets.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        /// <summary>
        /// A tracer for tracking blob content calls.
        /// </summary>
        protected readonly BackingContentStoreTracer Tracer;

        /// <summary>
        ///     Staging ground for parallel upload/downloads.
        /// </summary>
        protected readonly DisposableDirectory TempDirectory;

        /// <summary>
        ///     File system.
        /// </summary>
        protected readonly IAbsFileSystem FileSystem;

        // Error codes: https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
        private const int ErrorFileExists = 80;

        /// <summary>
        ///     Backing DedupStore client
        /// </summary>
        protected readonly IDedupStoreClient DedupStoreClient;

        /// <summary>
        ///     Gate to limit the number of oustanding connections to AS.
        /// </summary>
        protected readonly SemaphoreSlim ConnectionGate;

        /// <summary>
        ///     Expiration time of content in VSTS
        ///     Note: Determined by configurable timeToKeepContent. This is usually defined to be on the order of days.
        /// </summary>
        protected readonly DateTime EndDateTime;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobReadOnlyContentSession"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="name">Session name.</param>
        /// <param name="implicitPin">Policy determining whether or not content should be automatically pinned on adds or gets.</param>
        /// <param name="dedupStoreHttpClient">Backing DedupStore http client.</param>
        /// <param name="timeToKeepContent">Minimum time-to-live for accessed content.</param>
        /// <param name="tracer">A tracer for tracking blob content session calls.</param>
        /// <param name="maxConnections">The maximum number of outboud connections to VSTS.</param>
        public DedupReadOnlyContentSession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            IDedupStoreHttpClient dedupStoreHttpClient,
            TimeSpan timeToKeepContent,
            BackingContentStoreTracer tracer,
            int maxConnections = DefaultMaxConnections)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(name != null);
            Contract.Requires(dedupStoreHttpClient != null);

            Name = name;
            ImplicitPin = implicitPin;
            DedupStoreClient = new DedupStoreClient(dedupStoreHttpClient, DefaultMaxParallelism);
            Tracer = tracer;
            FileSystem = fileSystem;
            TempDirectory = new DisposableDirectory(fileSystem);
            ConnectionGate = new SemaphoreSlim(maxConnections);
            EndDateTime = DateTime.UtcNow + timeToKeepContent;
    }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose native resources.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedParameter.Global")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                TempDirectory.Dispose();
            }
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public async Task<PinResult> PinAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new PinResult($"DedupStore client requires {RequiredHashType}. Cannot take HashType '{contentHash.HashType}'.");
            }

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                Tracer.PinBulkStart(context, new[] { contentHash });

                var pinResult = CheckPinInMemory(contentHash);
                if (pinResult.Succeeded)
                {
                    return pinResult;
                }

                var dedupId = ToVstsBlobIdentifier(contentHash.ToBlobIdentifier()).ToDedupIdentifier();
                if (dedupId.AlgorithmId == Hashing.ChunkDedupIdentifier.ChunkAlgorithmId)
                {
                    pinResult = await TryPinChunkAsync(context, dedupId, cts);
                }
                else
                {
                    pinResult = await TryPinNodeAsync(context, dedupId, cts);
                }

                if (pinResult.Succeeded)
                {
                    BackingContentStoreExpiryCache.Instance.AddExpiry(contentHash, EndDateTime);
                }

                return pinResult;
            }
            catch (Exception ex)
            {
                return new PinResult(ex);
            }
            finally
            {
                Tracer.PinBulkStop(context, sw.Elapsed);
            }
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new OpenStreamResult($"DedupStore client requires {RequiredHashType}. Cannot take HashType '{contentHash.HashType}'.");
            }

            string tempFile = null;
            try
            {
                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                    if (!pinResult.Succeeded)
                    {
                        if (pinResult.Code == PinResult.ResultCode.ContentNotFound)
                        {
                            return new OpenStreamResult(null);
                        }
                        else
                        {
                            // Pin returned a service errror. Fail fast.
                            return new OpenStreamResult(pinResult);
                        }
                    }
                }

                tempFile = TempDirectory.CreateRandomFileName().Path;
                var result =
                    await PlaceFileInternalAsync(context, contentHash, tempFile, FileMode.Create, cts).ConfigureAwait(false);

                if (result.Succeeded)
                {
                    return new OpenStreamResult(new FileStream(
                        tempFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete,
                        StreamBufferSize,
                        FileOptions.DeleteOnClose));
                }

                return new OpenStreamResult(null);
            }
            catch (Exception e)
            {
                return new OpenStreamResult(e);
            }
            finally
            {
                if (tempFile != null)
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch (Exception e)
                    {
                        Tracer.Warning(context, $"Error deleting temporary file at {tempFile}: {e}");
                    }
                }
            }
        }

        /// <inheritdoc />
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
            if (contentHash.HashType != RequiredHashType)
            {
                return new PlaceFileResult($"DedupStore client requires {RequiredHashType}. Cannot take HashType '{contentHash.HashType}'.");
            }

            try
            {
                if (replacementMode != FileReplacementMode.ReplaceExisting && File.Exists(path.Path))
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }

                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                    if (!pinResult.Succeeded)
                    {
                        return pinResult.Code == PinResult.ResultCode.ContentNotFound
                            ? new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound)
                            : new PlaceFileResult(pinResult);
                    }
                }

                var fileMode = replacementMode == FileReplacementMode.ReplaceExisting
                    ? FileMode.Create
                    : FileMode.CreateNew;
                var placeResult =
                    await PlaceFileInternalAsync(context, contentHash, path.Path, fileMode, cts).ConfigureAwait(false);

                if (!placeResult.Succeeded)
                {
                    return new PlaceFileResult(placeResult, PlaceFileResult.ResultCode.NotPlacedContentNotFound);
                }

                var contentSize = GetContentSize(path);
                return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, contentSize);
            }
            catch (IOException e) when (IsErrorFileExists(e))
            {
                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
            }
            catch (Exception e)
            {
                return new PlaceFileResult(e);
            }
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(
            Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                Tracer.PinBulkStart(context, contentHashes);

                return await Workflows.RunWithFallback(
                    contentHashes,
                    hashes => CheckInMemoryCache(hashes),
                    hashes => UpdateDedupStoreAsync(context, hashes, cts),
                    result => result.Succeeded);
            }
            catch (Exception ex)
            {
                context.Warning($"Exception when querying pins against the VSTS services {ex}");
                return contentHashes.Select((_, index) => Task.FromResult(new PinResult(ex).WithIndex(index)));
            }
            finally
            {
                Tracer.PinBulkStop(context, sw.Elapsed);
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            // Also not implemented in BlobReadOnlyContentSession.
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <summary>
        /// Converts a ContentStore BlobId to an Artifact BlobId
        /// </summary>
        protected static VstsBlobIdentifier ToVstsBlobIdentifier(BlobIdentifier blobIdentifier)
        {
            return new VstsBlobIdentifier(blobIdentifier.Bytes);
        }

        private Task<BoolResult> PlaceFileInternalAsync(
            Context context, ContentHash contentHash, string path, FileMode fileMode, CancellationToken cts)
        {
            try
            {
                return GetFileWithDedupAsync(context, contentHash, path, cts);
            }
            catch (Exception e) when (fileMode == FileMode.CreateNew && !IsErrorFileExists(e))
            {
                try
                {
                    // Need to delete here so that a partial download doesn't run afoul of FileReplacementMode.FailIfExists upon retry
                    // Don't do this if the error itself was that the file already existed
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    Tracer.Warning(context, $"Error deleting file at {path}: {ex}");
                }

                throw;
            }
            catch (StorageException storageEx) when (storageEx.InnerException is WebException)
            {
                var webEx = (WebException)storageEx.InnerException;
                if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> UpdateDedupStoreAsync(
            Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts)
        {
            if (!contentHashes.Any())
            {
                return (new List<Task<Indexed<PinResult>>>()).AsEnumerable();
            }

            var dedupIdentifiers = contentHashes.Select(c => ToVstsBlobIdentifier(c.ToBlobIdentifier()).ToDedupIdentifier());

            var tryReferenceBlock = new TransformBlock<Indexed<VstsDedupIdentifier>, Indexed<PinResult>>(
               async i =>
               {
                   PinResult pinResult;

                   if (i.Item.AlgorithmId == Hashing.ChunkDedupIdentifier.ChunkAlgorithmId)
                   {
                       pinResult = await TryPinChunkAsync(context, i.Item, cts);
                   }
                   else
                   {
                       pinResult = await TryPinNodeAsync(context, i.Item, cts);
                   }

                   if (pinResult.Succeeded)
                   {
                       BackingContentStoreExpiryCache.Instance.AddExpiry(new ContentHash(HashType.DedupNodeOrChunk, i.Item.Value), EndDateTime);
                   }

                   return pinResult.WithIndex(i.Index);
               },
               new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DefaultMaxParallelism });

            tryReferenceBlock.PostAll(dedupIdentifiers.AsIndexed());
            var results = await Task.WhenAll(Enumerable.Range(0, dedupIdentifiers.ToList().Count).Select(i => tryReferenceBlock.ReceiveAsync()));
            tryReferenceBlock.Complete();

            return results.AsTasks().ToList();
        }

        private async Task<BoolResult> GetFileWithDedupAsync(Context context, ContentHash contentHash, string path, CancellationToken cts)
        {
            VstsBlobIdentifier blobId = ToVstsBlobIdentifier(contentHash.ToBlobIdentifier());
            VstsDedupIdentifier dedupId = blobId.ToDedupIdentifier();

            try
            {
                await TryGatedArtifactOperationAsync<Object>(
                    context,
                    contentHash.ToString(),
                    "DownloadToFileAsync",
                    async innerCts =>
                {
                    await DedupStoreClient.DownloadToFileAsync(dedupId, path, null, null, EdgeCache.Allowed, innerCts);
                    return null;
                }, cts);
            }
            catch (NullReferenceException) // Null reference thrown when DedupIdentifier doesn't exist in VSTS.
            {
                return new BoolResult("DedupIdentifier not found.");
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }

            return BoolResult.Success;
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> CheckInMemoryCache(IReadOnlyList<ContentHash> contentHashes)
        {
            return Task.FromResult(
                        contentHashes
                            .Select(c =>
                            {
                                if (c.HashType != RequiredHashType)
                                {
                                    return new PinResult($"DedupStore client requires {RequiredHashType}. Cannot take HashType '{c.HashType}'.");
                                }

                                return CheckPinInMemory(c);
                            })
                            .AsIndexedTasks());
        }

        private bool IsErrorFileExists(Exception e)
        {
            return (Marshal.GetHRForException(e) & ((1 << 16) - 1)) == ErrorFileExists;
        }

        private PinResult CheckPinInMemory(ContentHash contentHash)
        {
            DateTime expiryTime;

            // TODO: allow cached expiry time to be within some bump threshold (e.g. allow expiryTime = 6 days & endDateTime = 7 days) (bug 1365340)
            if (BackingContentStoreExpiryCache.Instance.TryGetExpiry(
                contentHash, out expiryTime) && expiryTime > EndDateTime)
            {
                Tracer.RecordPinSatisfiedInMemory();
                return PinResult.Success;
            }

            return PinResult.ContentNotFound;
        }

        /// <nodoc />
        protected long GetContentSize(AbsolutePath path)
        {
            var fileInfo = new FileInfo(path.Path);
            return fileInfo.Length;
        }

        /// <summary>
        /// Because pinning requires recursing an entire tree, we need to limit the number of simultaneous calls to DedupStore.
        /// </summary>
        protected async Task<TResult> TryGatedArtifactOperationAsync<TResult>(
            Context context, string content, string operationName, Func<CancellationToken, Task<TResult>> func, CancellationToken token, [CallerMemberName] string caller = null)
        {
            var sw = Stopwatch.StartNew();
            await ConnectionGate.WaitAsync(token);

            var elapsed = sw.Elapsed;

            if (elapsed.TotalSeconds >= MinLogWaitTimeInSeconds)
            {
                Tracer.Warning(context, $"Operation '{caller}' for {content} was throttled for {elapsed.TotalSeconds}sec");
            }

            try
            {
                return await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    operationName,
                    innerCts => func(innerCts),
                    token);
            }
            finally
            {
                ConnectionGate.Release();
            }
        }

        #region Internal Pin Methods
        /// <summary>
        /// Updates expiry of single chunk in DedupStore if it exists.
        /// </summary>
        private async Task<PinResult> TryPinChunkAsync(Context context, VstsDedupIdentifier dedupId, CancellationToken cts)
        {
            try
            {
                var receipt = await TryGatedArtifactOperationAsync(
                    context,
                    dedupId.ValueString,
                    "TryKeepUntilReferenceChunk",
                    innerCts => DedupStoreClient.Client.TryKeepUntilReferenceChunkAsync(dedupId.CastToChunkDedupIdentifier(), new KeepUntilBlobReference(EndDateTime), innerCts),
                    cts);

                if (receipt == null)
                {
                    return PinResult.ContentNotFound;
                }

                return PinResult.Success;
            }
            catch (Exception ex)
            {
                return new PinResult(ex);
            }
        }

        /// <summary>
        /// Updates expiry of single node in DedupStore if 
        ///     1) Node exists
        ///     2) All children exist and have sufficient TTL
        /// If children have insufficient TTL, attempt to extend the expiry of all children before pinning.
        /// </summary>
        private async Task<PinResult> TryPinNodeAsync(Context context, VstsDedupIdentifier dedupId, CancellationToken cts)
        {
            TryReferenceNodeResponse referenceResult;
            try
            {
                referenceResult = await TryGatedArtifactOperationAsync(
                    context,
                    dedupId.ValueString,
                    "TryKeepUntilReferenceNode",
                    innerCts => DedupStoreClient.Client.TryKeepUntilReferenceNodeAsync(dedupId.CastToNodeDedupIdentifier(), new KeepUntilBlobReference(EndDateTime), null, innerCts),
                    cts);
            }
            catch (DedupNotFoundException)
            {
                // When VSTS processes response, throws exception when node doesn't exist.
                referenceResult = new TryReferenceNodeResponse(new DedupNodeNotFound());
            }
            catch (Exception ex)
            {
                return new PinResult(ex);
            }

            PinResult pinResult = PinResult.ContentNotFound;

            referenceResult.Match(
                (notFound) =>
                {
                    // Root node has expired.
                },
                async (needAction) =>
                {
                    pinResult = await TryPinChildrenAsync(context, dedupId, needAction.InsufficientKeepUntil, cts);
                },
                (added) =>
                {
                    pinResult = PinResult.Success;
                });

            return pinResult;
        }

        /// <summary>
        /// Attempt to update expiry of all children. Pin parent node if all children were extended successfully.
        /// </summary>
        private async Task<PinResult> TryPinChildrenAsync(Context context, VstsDedupIdentifier parentNode, IEnumerable<VstsDedupIdentifier> dedupIdentifiers, CancellationToken cts)
        {
            var chunks = new List<VstsDedupIdentifier>();
            var nodes = new List<VstsDedupIdentifier>();

            foreach (var id in dedupIdentifiers)
            {
                if (id.AlgorithmId == Hashing.ChunkDedupIdentifier.ChunkAlgorithmId)
                {
                    chunks.Add(id);
                }
                else
                {
                    nodes.Add(id);
                }
            }

            // Attempt to save all children.
            Tracer.Debug(context, $"Pinning children: nodes=[{string.Join(",", nodes.Select(x => x.ValueString))}] chunks=[{string.Join(",", chunks.Select(x => x.ValueString))}]");
            var result = await TryPinNodesAsync(context, nodes, cts) & await TryPinChunksAsync(context, chunks, cts);
            if (result == PinResult.Success)
            {
                // If all children are saved, pin parent.
                result = await TryPinNodeAsync(context, parentNode, cts);
            }

            return result;
        }

        /// <summary>
        /// Recursively attempt to update expiry of all nodes and their children.
        /// Returns success only if all children of each node are found and extended.
        /// </summary>
        private async Task<PinResult> TryPinNodesAsync(Context context, IEnumerable<VstsDedupIdentifier> dedupIdentifiers, CancellationToken cts)
        {
            if (!dedupIdentifiers.Any())
            {
                return PinResult.Success;
            }

            // TODO: Support batched TryKeepUntilReferenceNodeAsync in Artifact. (bug 1428612)
            var tryReferenceBlock = new TransformBlock<VstsDedupIdentifier, PinResult>(
                async dedupId => await TryPinNodeAsync(context, dedupId, cts),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DefaultMaxParallelism });

            tryReferenceBlock.PostAll(dedupIdentifiers);
            var pinResults = await Task.WhenAll(Enumerable.Range(0, dedupIdentifiers.ToList().Count).Select(i => tryReferenceBlock.ReceiveAsync()));
            tryReferenceBlock.Complete();

            foreach (var result in pinResults)
            {
                if (!result.Succeeded)
                {
                    return result; // An error updating one of the nodes or its children occured. Fail fast.
                }
            }

            return PinResult.Success;
        }

        /// <summary>
        /// Update all chunks if they exist. Returns success only if all chunks are found and extended.
        /// </summary>
        private async Task<PinResult> TryPinChunksAsync(Context context, IEnumerable<VstsDedupIdentifier> dedupIdentifiers, CancellationToken cts)
        {
            if (!dedupIdentifiers.Any())
            {
                return PinResult.Success;
            }

            // TODO: Support batched TryKeepUntilReferenceChunkAsync in Artifact. (bug 1428612)
            var tryReferenceBlock = new TransformBlock<VstsDedupIdentifier, PinResult>(
                async dedupId => await TryPinChunkAsync(context, dedupId, cts),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = DefaultMaxParallelism });

            tryReferenceBlock.PostAll(dedupIdentifiers);
            var pinResults = await Task.WhenAll(Enumerable.Range(0, dedupIdentifiers.ToList().Count).Select(i => tryReferenceBlock.ReceiveAsync()));
            tryReferenceBlock.Complete();

            foreach (var result in pinResults)
            {
                if (!result.Succeeded)
                {
                    return result; // An error updating one of the chunks occured. Fail fast.
                }
            }

            return PinResult.Success;
        }
        #endregion
    }
}