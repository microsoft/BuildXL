// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Wrapper which passes common arguments to <see cref="CloudBlockBlob"/> APIs
    /// </summary>
    public record BlobWrapper(
        BlobFolderStorage Storage,
        CloudBlockBlob Blob,
        BlobName Name,
        CancellationToken Token,
        BlobRequestOptions Options,
        Microsoft.WindowsAzure.Storage.OperationContext? Context = null)
    {
        public AccessCondition? DefaultAccessCondition { get; set; }

        public T? GetMetadataOrDefault<T>(Func<string, T> parse, T? defaultValue = default, [CallerMemberName] string key = null!)
        {
            T parseCore(string value)
            {
                try
                {
                    return parse(value);
                }
                catch (FormatException ex)
                {
                    throw new FormatException($"Error parsing value '{value}' for key '{key}'", ex);
                }
            }

            return Blob.Metadata.TryGetValue(key, out var value) == true && !string.IsNullOrEmpty(value)
                ? parseCore(value)
                : defaultValue;
        }

        public void SetMetadata(string? value, [CallerMemberName] string key = null!)
        {
            if (value == null)
            {
                Blob.Metadata.Remove(key);
            }
            else if (Blob != null)
            {
                Blob.Metadata[key] = value;
            }
        }

        private AccessCondition? Apply(AccessCondition? condition)
        {
            return condition ?? DefaultAccessCondition;
        }

        internal Task<string> AcquireLeaseAsync(TimeSpan leaseTime, AccessCondition? accessCondition = null)
        {
            return Blob.AcquireLeaseAsync(leaseTime, proposedLeaseId: null, Apply(accessCondition), Options, Context, Token);
        }

        internal Task<IEnumerable<ListBlockItem>> DownloadBlockListAsync(BlockListingFilter filter, AccessCondition? accessCondition = null)
        {
            return Blob.DownloadBlockListAsync(filter, Apply(accessCondition), Options, Context, Token);
        }

        internal Task DownloadRangeToStreamAsync(Stream stream, long? offset, long? length, AccessCondition? accessCondition = null)
        {
            return Blob.DownloadRangeToStreamAsync(stream, offset, length, Apply(accessCondition), Options, Context, Token);
        }

        internal Task<bool> ExistsAsync()
        {
            return Blob.ExistsAsync(Options, Context, Token);
        }

        internal Task FetchAttributesAsync(AccessCondition? accessCondition = null)
        {
            return Blob.FetchAttributesAsync(Apply(accessCondition), Options, Context, Token);
        }

        internal Task PutBlockAsync(string blockId, Stream stream, AccessCondition? accessCondition = null, ContentHash? md5Hash = null)
        {
            Contract.Requires(md5Hash == null || md5Hash.Value.HashType == HashType.MD5);
            var contentMD5 = md5Hash == null ? null : Convert.ToBase64String(md5Hash.Value.ToHashByteArray());
            return Blob.PutBlockAsync(blockId, stream, contentMD5, Apply(accessCondition), Options, Context, Token);
        }

        internal Task PutBlockListAsync(IEnumerable<string> blockList, AccessCondition? accessCondition = null)
        {
            return Blob.PutBlockListAsync(blockList, Apply(accessCondition), Options, Context, Token);
        }

        internal Task ReleaseLeaseAsync(AccessCondition leaseCondition)
        {
            return Blob.ReleaseLeaseAsync(leaseCondition, Options, Context, Token);
        }

        internal Task UploadFromByteArrayAsync(ArraySegment<byte> buffer, AccessCondition? accessCondition = null)
        {
            return Blob.UploadFromByteArrayAsync(buffer.Array, buffer.Offset, buffer.Count, Apply(accessCondition), Options, Context, Token);
        }

        internal async Task<List<T>> ListSnapshotsAsync<T>(
            OperationContext context,
            Func<BlobWrapper, T> transformBlob)
        {
            return await Storage.ListBlobsAsync(
                context,
                regex: null,
                prefix: Name,
                listingSingleBlobSnapshots: true,
                blobListingDetails: BlobListingDetails.Metadata | BlobListingDetails.Snapshots)
                .Where(blob => blob.IsSnapshot)
                .OfType<CloudBlockBlob>()
                .Select(blob => transformBlob(Storage.WrapBlob(context.Token, Name with { SnapshotTime = blob.SnapshotTime }, blob)))
                .ToListAsync();
        }

        internal Task<bool> DeleteIfExistsAsync(DeleteSnapshotsOption option = DeleteSnapshotsOption.None, AccessCondition? accessCondition = null, CancellationToken? token = default)
        {
            return Blob.DeleteIfExistsAsync(option, Apply(accessCondition), Options, Context, token ?? Token);
        }

        internal async Task<BlobWrapper> SnapshotAsync(AccessCondition? accessCondition = null)
        {
            var result = await Blob.SnapshotAsync(metadata: null, Apply(accessCondition), Options, Context, Token);
            return Storage.GetBlob(Token, Name with { SnapshotTime = result.SnapshotTime });
        }
    }
}