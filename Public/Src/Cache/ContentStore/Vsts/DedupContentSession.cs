// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;
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
            int maxConnections = DefaultMaxConnections)
            : base(fileSystem, name, implicitPin, dedupStoreHttpClient, timeToKeepContent, maxConnections)
        {
            _artifactFileSystem = VstsFileSystem.Instance;
            _uploadSession = DedupStoreClient.CreateUploadSession(
                DedupStoreClient,
                new KeepUntilBlobReference(EndDateTime),
                new AppTraceSourceContextAdapter(context, "CreateUploadSession", SourceLevels.All),
                _artifactFileSystem);
        }

        /// <inheritdoc />
        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
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
                var pinResult = await PinAsync(context, contentHash, context.Token, urgencyHint);

                if (pinResult.Succeeded)
                {
                    return new PutResult(contentHash, contentSize);
                }

                if (pinResult.Code == PinResult.ResultCode.Error)
                {
                    return new PutResult(pinResult, contentHash);
                }

                var dedupNode = await GetDedupNodeFromFileAsync(path.Path, context.Token);
                var calculatedHash = dedupNode.ToContentHash();

                if (contentHash != calculatedHash)
                {
                    return new PutResult(
                        contentHash,
                        $"Failed to add a DedupStore reference due to hash mismatch: provided=[{contentHash}] calculated=[{calculatedHash}]");
                }

                var putResult = await UploadWithDedupAsync(context, path, dedupNode).ConfigureAwait(false);
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
        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
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
                var dedupNode = await GetDedupNodeFromFileAsync(path.Path, context.Token);
                var contentHash = dedupNode.ToContentHash();

                if (contentHash.HashType != hashType)
                {
                    return new PutResult(
                        contentHash,
                        $"Failed to add a DedupStore reference due to hash type mismatch: provided=[{hashType}] calculated=[{contentHash.HashType}]");
                }

                var pinResult = await PinAsync(context, contentHash, context.Token, urgencyHint);

                if (pinResult.Succeeded)
                {
                    return new PutResult(contentHash, contentSize);
                }

                if (pinResult.Code == PinResult.ResultCode.Error)
                {
                    return new PutResult(pinResult, contentHash);
                }

                var putResult = await UploadWithDedupAsync(context, path, dedupNode).ConfigureAwait(false);

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
        protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, ContentHash contentHash, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
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

                // Cast is necessary because ContentSessionBase implements IContentSession explicitly
                return await (this as IContentSession).PutFileAsync(context, contentHash, new AbsolutePath(tempFile), FileRealizationMode.None, context.Token, urgencyHint);
            }
            catch (Exception e)
            {
                return new PutResult(e, contentHash);
            }
        }

        /// <inheritdoc />
        protected override async Task<PutResult> PutStreamCoreAsync(OperationContext context, HashType hashType, Stream stream, UrgencyHint urgencyHint, Counter retryCounter)
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

                // Cast is necessary because ContentSessionBase implements IContentSession explicitly
                return await (this as IContentSession).PutFileAsync(context, hashType, new AbsolutePath(tempFile), FileRealizationMode.None, context.Token, urgencyHint);
            }
            catch (Exception e)
            {
                return new PutResult(e, new ContentHash(hashType));
            }
        }

        private async Task<BoolResult> UploadWithDedupAsync(
            OperationContext context,
            AbsolutePath path,
            DedupNode dedupNode)
        {
            // Puts are effectively implicitly pinned regardless of configuration.
            try
            {
                if (dedupNode.Type == DedupNode.NodeType.ChunkLeaf)
                {
                    await PutChunkAsync(context, dedupNode, path);
                }
                else
                {
                    await PutNodeAsync(context, dedupNode, path);
                }

                BackingContentStoreExpiryCache.Instance.AddExpiry(dedupNode.ToContentHash(), EndDateTime);
                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }
        }

        private async Task PutNodeAsync(OperationContext context, DedupNode dedupNode, AbsolutePath path)
        {
            var dedupIdentifier = dedupNode.GetDedupId();

            await TryGatedArtifactOperationAsync<object>(
                context,
                dedupIdentifier.ValueString,
                "DedupUploadSession.UploadAsync",
                async innerCts =>
                {
                    await _uploadSession.UploadAsync(dedupNode, new Dictionary<VstsDedupIdentifier, string> { { dedupIdentifier, path.Path } }, innerCts);
                    return null;
                });
        }

        private Task PutChunkAsync(OperationContext context, DedupNode dedupNode, AbsolutePath path)
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
                                innerCts));
        }

        private async Task<DedupNode> GetDedupNodeFromFileAsync(string path, CancellationToken token)
        {
            var dedupNode = await ChunkerHelper.CreateFromFileAsync(
                fileSystem: _artifactFileSystem,
                path: path,
                cancellationToken: token,
                configureAwait: false);

            return dedupNode;
        }
    }
}
