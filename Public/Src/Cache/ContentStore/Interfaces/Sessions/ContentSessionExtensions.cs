// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Extension methods for IContentSession.
    /// </summary>
    public static class ContentSessionExtensions
    {
        private const int MaxBulkPageSize = 16;

        /// <summary>
        ///     Put some random content into the store, up to some specified percent of maximum size.
        /// </summary>
        public static async Task<IReadOnlyList<ContentHash>> PutRandomAsync
            (
            this IContentSession session,
            Context context,
            HashType hashType,
            bool provideHash,
            long maxSize,
            int percent,
            long fileSize,
            bool useExactSize
            )
        {
            long bytesToPopulate = (percent * maxSize) / 100;

            if (useExactSize)
            {
                return await PutRandomAsync(
                    session, context, hashType, provideHash, (int)(bytesToPopulate / fileSize), fileSize, true).ConfigureAwait(false);
            }

            var contentHashes = new List<ContentHash>();
            long populatedSize = 0;
            while (populatedSize < bytesToPopulate)
            {
                var contentSizes = new List<int>();
                while (populatedSize < bytesToPopulate && contentSizes.Count < MaxBulkPageSize)
                {
                    var numBytes = (int)Math.Min(ThreadSafeRandom.Generator.Next((int)fileSize), bytesToPopulate - populatedSize);
                    contentSizes.Add(numBytes);
                    populatedSize += numBytes;
                }

                var tasks = new List<Task<PutResult>>(contentSizes.Count);
                tasks.AddRange(
                    contentSizes.Select(
                        contentSize =>
                            session.PutRandomAsync(context.CreateNested(), hashType, provideHash, contentSize, CancellationToken.None)));

                foreach (var task in tasks)
                {
                    var result = await task.ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        contentHashes.Add(result.ContentHash);
                    }
                }
            }

            return contentHashes;
        }

        /// <summary>
        ///     Put some random content files into the store.
        /// </summary>
        public static async Task<IReadOnlyList<ContentHash>> PutRandomAsync(
            this IContentSession session,
            Context context,
            HashType hashType,
            bool provideHash,
            int fileCount,
            long fileSize,
            bool useExactSize)
        {
            var contentHashes = new List<ContentHash>(fileCount);

            for (long i = 0; i < fileCount / MaxBulkPageSize; i++)
            {
                var hashes = await PutRandomBulkAsync(
                    session, context, hashType, provideHash, MaxBulkPageSize, fileSize, useExactSize).ConfigureAwait(false);
                contentHashes.AddRange(hashes);
            }

            var remainder = fileCount % MaxBulkPageSize;
            if (remainder > 0)
            {
                var hashes = await PutRandomBulkAsync(
                    session, context, hashType, provideHash, remainder, fileSize, useExactSize).ConfigureAwait(false);
                contentHashes.AddRange(hashes);
            }

            return contentHashes;
        }

        private static async Task<IReadOnlyList<ContentHash>> PutRandomBulkAsync(
            this IContentSession session,
            Context context,
            HashType hashType,
            bool provideHash,
            int fileCount,
            long fileSize,
            bool useExactSize)
        {
            Contract.Requires(fileCount > 0);

            var c = context.CreateNested();
            var tasks = Enumerable.Range(0, fileCount).Select(_ => Task.Run(async () => await session.PutRandomAsync(
                c,
                hashType,
                provideHash,
                useExactSize ? fileSize : ThreadSafeRandom.Generator.Next(1, (int)fileSize),
                CancellationToken.None)
                .ConfigureAwait(false))).ToList();

            var contentHashes = new List<ContentHash>(fileCount);
            foreach (var task in tasks.ToList())
            {
                var result = await task.ConfigureAwait(false);
                if (result.Succeeded)
                {
                    contentHashes.Add(result.ContentHash);
                }
            }

            return contentHashes;
        }

        /// <summary>
        /// Put a randomly-sized piece of content into the store.
        /// </summary>
        public static async Task<PutResult> PutRandomAsync(
            this IContentSession session,
            Context context,
            HashType hashType,
            bool provideHash,
            long size,
            CancellationToken ct)
        {
            Contract.RequiresNotNull(session);
            Contract.RequiresNotNull(context);

            var c = context.CreateNested();

            // TODO: Fix this to work with size > int.Max (bug 1365340)
            var data = ThreadSafeRandom.GetBytes((int)size);

            using (var stream = new MemoryStream(data))
            {
                if (!provideHash)
                {
                    return await session.PutStreamAsync(c, hashType, stream, ct).ConfigureAwait(false);
                }

                var hash = HashInfoLookup.Find(hashType).CreateContentHasher().GetContentHash(data);
                return await session.PutStreamAsync(c, hash, stream, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Put a randomly-sized piece of content into the store.
        /// </summary>
        public static async Task<PutResult> PutContentAsync(
            this IContentSession session, Context context, string content)
        {
            var c = context.CreateNested();

            var data = Encoding.UTF8.GetBytes(content);
            var hashType = HashType.SHA256;
            using (var stream = new MemoryStream(data))
            {
                var hash = HashInfoLookup.Find(hashType).CreateContentHasher().GetContentHash(data);
                return await session.PutStreamAsync(c, hash, stream, CancellationToken.None).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Put a randomly-sized content from a file into the store.
        /// </summary>
        public static async Task<PutResult> PutRandomFileAsync(
            this IContentSession session,
            Context context,
            IAbsFileSystem fileSystem,
            HashType hashType,
            bool provideHash,
            long size,
            CancellationToken ct)
        {
            Contract.RequiresNotNull(session);
            Contract.RequiresNotNull(context);
            Contract.RequiresNotNull(fileSystem);

            using (var directory = new DisposableDirectory(fileSystem))
            {
                var c = context.CreateNested();

                // TODO: Fix this to work with size > int.Max (bug 1365340)
                var data = ThreadSafeRandom.GetBytes((int)size);
                var path = directory.CreateRandomFileName();
                fileSystem.WriteAllBytes(path, data);

                if (!provideHash)
                {
                    return await session.PutFileAsync(c, hashType, path, FileRealizationMode.Any, ct).ConfigureAwait(false);
                }

                var hash = HashInfoLookup.Find(hashType).CreateContentHasher().GetContentHash(data);
                return await session.PutFileAsync(c, hash, path, FileRealizationMode.Any, ct).ConfigureAwait(false);
            }
        }
    }
}
