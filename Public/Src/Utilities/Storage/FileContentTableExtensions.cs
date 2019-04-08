// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Storage
{
    /// <summary>
    /// Extensions for common applications of <see cref="FileContentTable" />.
    /// </summary>
    public static class FileContentTableExtensions
    {
        /// <summary>
        /// Indicates if a retrieved content hash was cached (in a <see cref="FileContentTable" />) or computed just now.
        /// </summary>
        public enum ContentHashOrigin : byte
        {
            /// <summary>
            /// The content hash was computed just now.
            /// </summary>
            Cached,

            /// <summary>
            /// The content hash was already known (cached in a <see cref="FileContentTable"/>).
            /// </summary>
            NewlyHashed,
        }

        /// <summary>
        /// Pair of a <see cref="VersionedFileIdentityAndContentInfo"/> and its origin (cached or newly hashed). This is the return value of
        /// </summary>
        public readonly struct VersionedFileIdentityAndContentInfoWithOrigin : IEquatable<VersionedFileIdentityAndContentInfoWithOrigin>
        {
            /// <summary>
            /// Hash of the file and file identity
            /// </summary>
            public readonly VersionedFileIdentityAndContentInfo VersionedFileIdentityAndContentInfo;

            /// <summary>
            /// Means by which the <see cref="VersionedFileIdentityAndContentInfo"/> was determined
            /// </summary>
            public readonly ContentHashOrigin Origin;

            /// <obvious />
            public VersionedFileIdentityAndContentInfoWithOrigin(VersionedFileIdentityAndContentInfo identityAndContentInfo, ContentHashOrigin origin)
            {
                VersionedFileIdentityAndContentInfo = identityAndContentInfo;
                Origin = origin;
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(VersionedFileIdentityAndContentInfo.GetHashCode(), Origin.GetHashCode());
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                return StructUtilities.Equals(this, obj);
            }

            /// <inheritdoc />
            public bool Equals(VersionedFileIdentityAndContentInfoWithOrigin other)
            {
                return other.VersionedFileIdentityAndContentInfo == VersionedFileIdentityAndContentInfo && other.Origin == Origin;
            }

            /// <nodoc />
            public static bool operator ==(VersionedFileIdentityAndContentInfoWithOrigin left, VersionedFileIdentityAndContentInfoWithOrigin right)
            {
                return left.Equals(right);
            }

            /// <nodoc />
            public static bool operator !=(VersionedFileIdentityAndContentInfoWithOrigin left, VersionedFileIdentityAndContentInfoWithOrigin right)
            {
                return !left.Equals(right);
            }
        }

        /// <summary>
        /// Retrieves a hash for the given file, either cached from this file content table or by hashing the file.
        /// If the file must be hashed (cache miss), the computed hash is stored in the file content table.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if accessing the local path specified by 'key' fails.</exception>
        public static async Task<VersionedFileIdentityAndContentInfoWithOrigin> GetAndRecordContentHashAsync(
            this FileContentTable fileContentTable,
            string path,
            bool? strict = default,
            Action<SafeFileHandle, VersionedFileIdentityAndContentInfoWithOrigin> beforeClose = null)
        {
            Contract.Requires(fileContentTable != null);
            Contract.Requires(path != null);

            if (beforeClose == null)
            {
                // Due to path mapping in FileContentTable, querying with path will be much faster than opening a handle for a stream.
                VersionedFileIdentityAndContentInfo? existingInfo = fileContentTable.TryGetKnownContentHash(path);

                if (existingInfo.HasValue)
                {
                    return new VersionedFileIdentityAndContentInfoWithOrigin(existingInfo.Value, ContentHashOrigin.Cached);
                }
            }

            using (
                FileStream contentStream = FileUtilities.CreateFileStream(
                    path,
                    FileMode.Open,
                    strict == true ? FileAccess.ReadWrite : FileAccess.Read,
                    FileShare.Read | FileShare.Delete,
                    FileOptions.None,
                    force: strict == true))
            {
                VersionedFileIdentityAndContentInfoWithOrigin newInfo = await fileContentTable.GetAndRecordContentHashAsync(
                    contentStream,
                    strict: strict,
                    ignoreKnownContentHash: beforeClose == null);

                beforeClose?.Invoke(contentStream.SafeFileHandle, newInfo);

                return newInfo;
            }
        }

        /// <summary>
        /// Retrieves a hash for the given file, either cached from this file content table or by hashing the file.
        /// If the file must be hashed (cache miss), the computed hash is stored in the file content table.
        /// </summary>
        /// <exception cref="BuildXLException">Thrown if accessing the local path specified by 'key' fails.</exception>
        public static async Task<VersionedFileIdentityAndContentInfoWithOrigin> GetAndRecordContentHashAsync(
            this FileContentTable fileContentTable,
            FileStream contentStream,
            bool? strict = default, 
            bool ignoreKnownContentHash = false)
        {
            Contract.Requires(fileContentTable != null);
            Contract.Requires(contentStream != null);

            if (!ignoreKnownContentHash)
            {
                VersionedFileIdentityAndContentInfo? existingInfo = fileContentTable.TryGetKnownContentHash(contentStream);

                if (existingInfo.HasValue)
                {
                    return new VersionedFileIdentityAndContentInfoWithOrigin(existingInfo.Value, ContentHashOrigin.Cached);
                }
            }

            ContentHash newHash = await ContentHashingUtilities.HashContentStreamAsync(contentStream);
            VersionedFileIdentityAndContentInfo newInfo = fileContentTable.RecordContentHash(
                contentStream, 
                newHash,
                strict: strict);

            return new VersionedFileIdentityAndContentInfoWithOrigin(newInfo, ContentHashOrigin.NewlyHashed);
        }

        /// <summary>
        /// Performs a smart copy in which no writes are performed if the destination already has the same content as the source
        /// (as provided in <paramref name="sourceContentInfo" />).
        /// Note that the destination may be replaced if it exists (otherwise there's no use in comparing hashes).
        /// </summary>
        /// <remarks>
        /// Note that <paramref name="sourceContentInfo" /> should be faithful to <paramref name="sourcePath" />, since that hash is
        /// to be recorded for <paramref name="destinationPath" />.
        /// </remarks>
        /// <returns>Indicates if the copy was elided (up-to-date) or actually performed.</returns>
        public static async Task<ConditionalUpdateResult> CopyIfContentMismatchedAsync(
            this FileContentTable fileContentTable,
            string sourcePath,
            string destinationPath,
            FileContentInfo sourceContentInfo)
        {
            Contract.Requires(!string.IsNullOrEmpty(sourcePath));
            Contract.Requires(!string.IsNullOrEmpty(destinationPath));

            VersionedFileIdentityAndContentInfo? destinationInfo = null;

            bool copied = await FileUtilities.CopyFileAsync(
                sourcePath,
                destinationPath,
                predicate: (source, dest) =>
                                 {
                                     // Nonexistent destination?
                                     if (dest == null)
                                     {
                                         return true;
                                     }

                                     VersionedFileIdentityAndContentInfo? knownDestinationInfo = fileContentTable.TryGetKnownContentHash(destinationPath, dest);
                                     if (!knownDestinationInfo.HasValue || knownDestinationInfo.Value.FileContentInfo.Hash != sourceContentInfo.Hash)
                                     {
                                         return true;
                                     }

                                     destinationInfo = knownDestinationInfo.Value;
                                     return false;
                                 },
                onCompletion: (source, dest) =>
                              {
                                  Contract.Assume(
                                      destinationInfo == null,
                                      "onCompletion should only happen when we committed to a copy (and then, we shouldn't have a destination version yet).");
                                  VersionedFileIdentity identity =
                                      fileContentTable.RecordContentHash(
                                        destinationPath,
                                        dest,
                                        sourceContentInfo.Hash,
                                        sourceContentInfo.Length,
                                        strict: true);
                                  destinationInfo = new VersionedFileIdentityAndContentInfo(identity, sourceContentInfo);
                              });

            Contract.Assume(destinationInfo != null);
            return new ConditionalUpdateResult(!copied, destinationInfo.Value);
        }

        /// <summary>
        /// Performs a smart write in which no write is performed if the destination already has the same content as the source
        /// (as provided in <paramref name="contentsHash" />).
        /// Note that the destination may be replaced if it exists (otherwise there's no use in comparing hashes).
        /// </summary>
        /// <remarks>
        /// Note that <paramref name="contentsHash" /> must be faithful to <paramref name="contents" />, since that hash is
        /// recorded for <paramref name="destinationPath" />
        /// if a copy is performed.
        /// </remarks>
        /// <returns>A bool indicating if the content was mismatched and thus a full write was performed.</returns>
        public static async Task<ConditionalUpdateResult> WriteBytesIfContentMismatchedAsync(
            this FileContentTable fileContentTable,
            string destinationPath,
            byte[] contents,
            ContentHash contentsHash)
        {
            Contract.Requires(fileContentTable != null);
            Contract.Requires(!string.IsNullOrEmpty(destinationPath));
            Contract.Requires(contents != null);

            VersionedFileIdentityAndContentInfo? destinationInfo = null;

            bool written = await FileUtilities.WriteAllBytesAsync(
                destinationPath,
                contents,
                predicate: handle =>
                {
                    // Nonexistent destination?
                    if (handle == null)
                    {
                        return true;
                    }

                    VersionedFileIdentityAndContentInfo? known = fileContentTable.TryGetKnownContentHash(destinationPath, handle);

                    // We return true (proceed) if there's a hash mismatch.
                    if (!known.HasValue || known.Value.FileContentInfo.Hash != contentsHash)
                    {
                        return true;
                    }

                    destinationInfo = known.Value;
                    return false;
                },
                onCompletion: handle =>
                              {
                                  Contract.Assume(destinationInfo == null);
                                  VersionedFileIdentity identity =
                                    fileContentTable.RecordContentHash(
                                        destinationPath,
                                        handle,
                                        contentsHash,
                                        contents.Length,
                                        strict: true);
                                  destinationInfo = new VersionedFileIdentityAndContentInfo(
                                      identity,
                                      new FileContentInfo(contentsHash, contents.Length));
                              });

            Contract.Assume(destinationInfo != null);
            return new ConditionalUpdateResult(!written, destinationInfo.Value);
        }
    }

    /// <summary>
    /// Result of performing a conditional 'if mismatched' update; <see cref="FileContentTableExtensions.WriteBytesIfContentMismatchedAsync"/>.
    /// </summary>
    public sealed class ConditionalUpdateResult
    {
        internal ConditionalUpdateResult(bool elided, VersionedFileIdentityAndContentInfo destinationInfo)
        {
            Elided = elided;
            DestinationInfo = destinationInfo;
        }

        /// <summary>
        /// Indicates if the update was elided due to the destination already having the specified content.
        /// </summary>
        public bool Elided { get; }

        /// <summary>
        /// Identity and hash of the destination.
        /// </summary>
        public VersionedFileIdentityAndContentInfo DestinationInfo { get; }
    }
}
