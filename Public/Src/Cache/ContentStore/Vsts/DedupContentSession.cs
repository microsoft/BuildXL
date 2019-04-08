// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using VstsDedupIdentifier = Microsoft.VisualStudio.Services.BlobStore.Common.DedupIdentifier;
using VstsFileSystem = Microsoft.VisualStudio.Services.Content.Common.FileSystem;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// IContentSession for DedupContentStore.
    /// </summary>
    public class DedupContentSession : DedupReadOnlyContentSession, IContentSession
    {
        private readonly VstsFileSystem _artifactFileSystem;
        private readonly IDedupUploadSession _uploadSession;

        /// <summary>
        /// Initializes a new instance of the <see cref="DedupContentSession"/> class.
        /// </summary>
        public DedupContentSession(
            Context context,
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            IDedupStoreHttpClient dedupStoreHttpClient,
            TimeSpan timeToKeepContent,
            BackingContentStoreTracer tracer,
            int maxConnections = DefaultMaxConnections)
            : base(fileSystem, name, implicitPin, dedupStoreHttpClient, timeToKeepContent, tracer, maxConnections)
        {
            _artifactFileSystem = VstsFileSystem.Instance;
            _uploadSession = DedupStoreClient.CreateUploadSession(
                DedupStoreClient,
                new KeepUntilBlobReference(EndDateTime),
                new AppTraceSourceContextAdapter(context, "CreateUploadSession", SourceLevels.All),
                _artifactFileSystem);
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new PutResult(
                    contentHash,
                    $"DedupStore client requires {RequiredHashType}. Cannot take HashType '{contentHash.HashType}'.");
            }

            try
            {
                long contentSize = GetContentSize(path);
                PinResult pinResult = await PinAsync(context, contentHash, cts, urgencyHint);

                if (pinResult.Succeeded)
                {
                    return new PutResult(contentHash, contentSize);
                }

                if (pinResult.Code == PinResult.ResultCode.Error)
                {
                    return new PutResult(pinResult, contentHash);
                }

                var dedupNode = await GetDedupNodeFromFileAsync(path.Path, cts);
                var calculatedHash = dedupNode.ToContentHash();

                if (contentHash != calculatedHash)
                {
                    return new PutResult(
                        contentHash,
                        $"Failed to add a DedupStore reference due to hash mismatch: provided=[{contentHash}] calculated=[{calculatedHash}]");
                }

                var putResult = await UploadWithDedupAsync(context, path, dedupNode, cts, urgencyHint).ConfigureAwait(false);
                if (!putResult.Succeeded)
                {
                    return new PutResult(
                        putResult,
                        contentHash,
                        $"Failed to add a DedupStore reference to content with hash=[{contentHash}]");
                }

                return new PutResult(contentHash, contentSize);
            }
            catch (Exception e)
            {
                return new PutResult(e, contentHash);
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (hashType != RequiredHashType)
            {
                return new PutResult(
                    new ContentHash(hashType),
                    $"DedupStore client requires {RequiredHashType}. Cannot take HashType '{hashType}'.");
            }

            try
            {
                var contentSize = GetContentSize(path);
                var dedupNode = await GetDedupNodeFromFileAsync(path.Path, cts);
                var contentHash = dedupNode.ToContentHash();

                if (contentHash.HashType != hashType)
                {
                    return new PutResult(
                        contentHash,
                        $"Failed to add a DedupStore reference due to hash type mismatch: provided=[{hashType}] calculated=[{contentHash.HashType}]");
                }

                PinResult pinResult = await PinAsync(context, contentHash, cts, urgencyHint);

                if (pinResult.Succeeded)
                {
                    return new PutResult(contentHash, contentSize);
                }

                if (pinResult.Code == PinResult.ResultCode.Error)
                {
                    return new PutResult(pinResult, contentHash);
                }

                var putResult = await UploadWithDedupAsync(context, path, dedupNode, cts, urgencyHint).ConfigureAwait(false);

                if (!putResult.Succeeded)
                {
                    return new PutResult(
                        putResult,
                        contentHash,
                        $"Failed to add a DedupStore reference to content with hash=[{contentHash}]");
                }

                return new PutResult(contentHash, contentSize);
            }
            catch (Exception e)
            {
                return new PutResult(e, new ContentHash(hashType));
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutStreamAsync(Context context, ContentHash contentHash, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new PutResult(
                    contentHash,
                    $"DedupStore client requires {RequiredHashType}. Cannot take HashType '{contentHash.HashType}'.");
            }

            try
            {
                var tempFile = TempDirectory.CreateRandomFileName().Path;
                using (Stream writer = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.CopyToAsync(writer);
                }

                return await PutFileAsync(context, contentHash, new AbsolutePath(tempFile), FileRealizationMode.None, cts, urgencyHint);
            }
            catch (Exception e)
            {
                return new PutResult(e, contentHash);
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutStreamAsync(Context context, HashType hashType, Stream stream, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            if (hashType != RequiredHashType)
            {
                return new PutResult(
                    new ContentHash(hashType),
                    $"DedupStore client requires {RequiredHashType}. Cannot take HashType '{hashType}'.");
            }

            try
            {
                var tempFile = TempDirectory.CreateRandomFileName().Path;
                using (Stream writer = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await stream.CopyToAsync(writer);
                }

                return await PutFileAsync(context, hashType, new AbsolutePath(tempFile), FileRealizationMode.None, cts, urgencyHint);
            }
            catch (Exception e)
            {
                return new PutResult(e, new ContentHash(hashType));
            }
        }

        private async Task<BoolResult> UploadWithDedupAsync(
            Context context,
            AbsolutePath path,
            DedupNode dedupNode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            // Puts are effectively implicitly pinned regardless of configuration.
            try
            {
                if (dedupNode.Type == DedupNode.NodeType.ChunkLeaf)
                {
                    await PutChunkAsync(context, dedupNode, path, cts);
                }
                else
                {
                    await PutNodeAsync(context, dedupNode, path, cts);
                }

                BackingContentStoreExpiryCache.Instance.AddExpiry(dedupNode.ToContentHash(), EndDateTime);
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }
        }

        private async Task PutNodeAsync(Context context, DedupNode dedupNode, AbsolutePath path, CancellationToken cts)
        {
            var dedupIdentifier = dedupNode.GetDedupId();

            await TryGatedArtifactOperationAsync<Object>(
                context,
                dedupIdentifier.ValueString,
                "DedupUploadSession.UploadAsync",
                async innerCts =>
                {
                    await _uploadSession.UploadAsync(dedupNode, new Dictionary<VstsDedupIdentifier, string> { { dedupIdentifier, path.Path } }, innerCts);
                    return null;
                }, cts);
        }

        private Task PutChunkAsync(Context context, DedupNode dedupNode, AbsolutePath path, CancellationToken cts)
        {
            var dedupIdentifier = dedupNode.GetDedupId();
            return TryGatedArtifactOperationAsync(
                context,
                dedupIdentifier.ValueString,
                "PutChunkAndKeepUntilReferenceAsync",
                innerCts => DedupStoreClient.Client.PutChunkAndKeepUntilReferenceAsync(
                                dedupIdentifier.CastToChunkDedupIdentifier(),
                                DedupCompressedBuffer.FromUncompressed(File.ReadAllBytes(path.Path)),
                                new KeepUntilBlobReference(EndDateTime),
                                innerCts),
                cts);
        }

        private async Task<DedupNode> GetDedupNodeFromFileAsync(string path, CancellationToken cts)
        {
            var dedupNode = await ChunkerHelper.CreateFromFileAsync(
                fileSystem: _artifactFileSystem,
                path: path,
                cancellationToken: cts,
                configureAwait: false);

            return dedupNode;
        }
    }
}
