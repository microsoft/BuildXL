// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
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
using Microsoft.VisualStudio.Services.Content.Common;
using FileInfo = System.IO.FileInfo;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// IContentSession for BlobContentStore.
    /// </summary>
    public class BlobContentSession : BlobReadOnlyContentSession, IContentSession
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BlobContentSession" /> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="name">Session name.</param>
        /// <param name="implicitPin">Policy determining whether or not content should be automatically pinned on adds or gets.</param>
        /// <param name="blobStoreHttpClient">Backing BlobStore http client.</param>
        /// <param name="timeToKeepContent">Minimum time-to-live for accessed content.</param>
        /// <param name="tracer">A tracer for tracking blob content calls</param>
        /// <param name="downloadBlobsThroughBlobStore">
        /// If true, gets blobs through BlobStore. If false, gets blobs from the Azure
        /// Uri.
        /// </param>
        public BlobContentSession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            IBlobStoreHttpClient blobStoreHttpClient,
            TimeSpan timeToKeepContent,
            BackingContentStoreTracer tracer,
            bool downloadBlobsThroughBlobStore)
            : base(fileSystem, name, implicitPin, blobStoreHttpClient, timeToKeepContent, tracer, downloadBlobsThroughBlobStore)
        {
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            if (hashType != RequiredHashType)
            {
                return new PutResult(
                    new ContentHash(hashType),
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{hashType}'.");
            }

            try
            {
                long contentSize;
                ContentHash contentHash;
                using (var hashingStream = new FileStream(
                    path.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    StreamBufferSize))
                {
                    contentSize = hashingStream.Length;
                    contentHash = await HashInfoLookup.Find(hashType).CreateContentHasher().GetContentHashAsync(hashingStream).ConfigureAwait(false);
                }

                using (var streamToPut = FileStreamUtils.OpenFileStreamForAsync(
                    path.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    BoolResult putSucceeded = await PutLazyStreamAsync(
                        context,
                        contentHash,
                        streamToPut,
                        cts,
                        urgencyHint).ConfigureAwait(false);

                    if (!putSucceeded.Succeeded)
                    {
                        return new PutResult(
                            putSucceeded,
                            contentHash,
                            $"Failed to add a BlobStore reference to content with hash=[{contentHash}]");
                    }
                }

                return new PutResult(contentHash, contentSize);
            }
            catch (Exception e)
            {
                return new PutResult(e, new ContentHash(hashType));
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new PutResult(
                    contentHash,
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{contentHash.HashType}'.");
            }

            try
            {
                var fileInfo = new FileInfo(path.Path);
                var contentSize = fileInfo.Length;

                using (FileStream streamToPut = FileStreamUtils.OpenFileStreamForAsync(
                    path.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    BoolResult putResult = await PutLazyStreamAsync(context, contentHash, streamToPut, cts, urgencyHint).ConfigureAwait(false);

                    if (!putResult.Succeeded)
                    {
                        return new PutResult(
                            putResult,
                            contentHash,
                            $"Failed to add a BlobStore reference to content with hash=[{contentHash}]");
                    }
                }

                return new PutResult(contentHash, contentSize);
            }
            catch (Exception e)
            {
                return new PutResult(e, contentHash);
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutStreamAsync(
            Context context,
            HashType hashType,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            if (hashType != RequiredHashType)
            {
                return new PutResult(
                    new ContentHash(hashType),
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{hashType}'.");
            }

            try
            {
                Stream streamToPut = stream;

                // Can't assume we've been given a seekable stream.
                if (!stream.CanSeek)
                {
                    streamToPut = await CreateSeekableStream(context, stream);
                }

                using (streamToPut)
                {
                    Contract.Assert(streamToPut.CanSeek);
                    long contentSize = streamToPut.Length;
                    ContentHash contentHash = await HashInfoLookup.Find(hashType)
                        .CreateContentHasher()
                        .GetContentHashAsync(streamToPut)
                        .ConfigureAwait(false);
                    streamToPut.Seek(0, SeekOrigin.Begin);
                    var putResult =
                        await PutLazyStreamAsync(context, contentHash, streamToPut, cts, urgencyHint).ConfigureAwait(false);
                    if (!putResult.Succeeded)
                    {
                        return new PutResult(putResult, contentHash, $"Failed to add a BlobStore reference to content with hash=[{contentHash}]");
                    }

                    return new PutResult(contentHash, contentSize);
                }
            }
            catch (Exception e)
            {
                return new PutResult(e, new ContentHash(hashType));
            }
        }

        /// <inheritdoc />
        public async Task<PutResult> PutStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new PutResult(
                    contentHash,
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{contentHash.HashType}'.");
            }

            try
            {
                Stream streamToPut = stream;
                if (!stream.CanSeek)
                {
                    streamToPut = await CreateSeekableStream(context, streamToPut);
                }

                long streamLength = streamToPut.Length;

                var putResult =
                    await PutLazyStreamAsync(context, contentHash, streamToPut, cts, urgencyHint).ConfigureAwait(false);

                if (!putResult.Succeeded)
                {
                    return new PutResult(
                        putResult,
                        contentHash,
                        $"Failed to add a BlobStore reference to content with hash=[{contentHash}]");
                }

                return new PutResult(contentHash, streamLength);
            }
            catch (Exception e)
            {
                return new PutResult(e, contentHash);
            }
        }

        private async Task<Stream> CreateSeekableStream(Context context, Stream stream)
        {
            // Must stream to a temp location to get the hash before streaming it to BlobStore
            string tempFile = TempDirectory.CreateRandomFileName().Path;
            try
            {
                using (var output = new FileStream(
                    tempFile,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read | FileShare.Delete,
                    StreamBufferSize))
                {
                    await stream.CopyToAsync(output);
                }
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(tempFile))
                    {
                        File.Delete(tempFile);
                    }
                }
                catch (Exception deleteEx)
                {
                    context.Error($"Failed to delete temp file {tempFile} on error with failure {deleteEx}");
                }

                throw;
            }

            return new FileStream(
                tempFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                StreamBufferSize,
                FileOptions.DeleteOnClose);
        }

        // ReSharper disable once RedundantOverridenMember

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
        }

        private async Task<BoolResult> PutLazyStreamAsync(
            Context context,
            ContentHash contentHash,
            Stream stream,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            PinResult pinResult = await PinAsync(context, contentHash, cts, urgencyHint);

            if (pinResult.Code == PinResult.ResultCode.Success)
            {
                return BoolResult.Success;
            }

            // Puts are effectively implicitly pinned regardless of configuration.
            try
            {
                DateTime endDateTime = DateTime.UtcNow + TimeToKeepContent;
                await BlobStoreHttpClient.UploadAndReferenceBlobWithRetriesAsync(
                    ToVstsBlobIdentifier(contentHash.ToBlobIdentifier()),
                    stream,
                    new BlobReference(endDateTime),
                    context,
                    cts);

                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }
        }
    }
}
