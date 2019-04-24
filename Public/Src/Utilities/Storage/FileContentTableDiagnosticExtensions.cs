// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Native.IO;
using BuildXL.Storage.FileContentTableAccessor;

namespace BuildXL.Storage.Diagnostics
{
    /// <summary>
    /// Extensions for <see cref="FileContentTable" /> introspection and consistency checks.
    /// </summary>
    public static class FileContentTableDiagnosticExtensions
    {
        /// <summary>
        /// Indicates that a particular file identified by (equivalently) <see cref="Path" /> and <see cref="FileIdAndVolumeId" />
        /// has an actual content hash that disagrees with the content hash in a <see cref="FileContentTable" /> (despite the
        /// current
        /// <see cref="Usn" /> being associated with that known hash).
        /// </summary>
        public sealed class IncorrectFileContentEntry
        {
            /// <summary>
            /// Windows or NT-style path to the file.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Pair of file ID and volume ID which fully identifies the file.
            /// </summary>
            /// <remarks>
            /// This is the actual key in the file content table (path is synthesized on the fly).
            /// </remarks>
            public readonly FileIdAndVolumeId FileIdAndVolumeId;

            /// <summary>
            /// USN for both the known file content table entry and the file on disk presently.
            /// </summary>
            public readonly Usn Usn;

            /// <summary>
            /// Expected hash as present in the file content table.
            /// </summary>
            public readonly ContentHash ExpectedHash;

            /// <summary>
            /// Actual hash as observed on disk (not equal to the <see cref="ExpectedHash" />).
            /// </summary>
            public readonly ContentHash ActualHash;

            /// <nodoc />
            public IncorrectFileContentEntry(
                string path,
                FileIdAndVolumeId fileIdAndVolumeId,
                Usn usn,
                ContentHash expectedHash,
                ContentHash actualHash)
            {
                Contract.Requires(expectedHash != actualHash);
                Contract.Requires(!string.IsNullOrEmpty(path));

                Path = path;
                FileIdAndVolumeId = fileIdAndVolumeId;
                Usn = usn;
                ExpectedHash = expectedHash;
                ActualHash = actualHash;
            }
        }

        /// <summary>
        /// Returns a possibly-empty list of incorrect entries in this <see cref="FileContentTable" />.
        /// </summary>
        /// <remarks>
        /// In the absence of an incorrect file content table implementation, file system bugs, or disk corruption, the list should
        /// be empty.
        /// Hence, this is a diagnostic facility.
        /// </remarks>
        public static List<IncorrectFileContentEntry> FindIncorrectEntries(this FileContentTable fileContentTable, IFileContentTableAccessor accessor)
        {
            Contract.Requires(fileContentTable != null);
            Contract.Requires(accessor != null);
            Contract.Ensures(Contract.Result<List<IncorrectFileContentEntry>>() != null);
            Contract.Ensures(Contract.ForAll(Contract.Result<List<IncorrectFileContentEntry>>(), e => e != null));

            var badEntries = new List<IncorrectFileContentEntry>();
            fileContentTable.VisitKnownFiles(
                accessor,
                FileShare.Read | FileShare.Delete,
                // On accessing the entry of file content table, we do not allow concurrent writes; otherwise the content may change as we're hashing it.
                // To this end, the file needs to be opened with FileShare.Read | FileShare.Delete accesses.
                (fileIdAndVolumeId, handle, path, knownUsn, knownHash) =>
                {
                    using (var fs = new FileStream(handle, FileAccess.Read))
                    {
                        ContentHash actualHash = ContentHashingUtilities.HashContentStreamAsync(fs).GetAwaiter().GetResult();
                        if (knownHash != actualHash)
                        {
                            badEntries.Add(new IncorrectFileContentEntry(path, fileIdAndVolumeId, knownUsn, knownHash, actualHash));
                        }
                    }

                    return true;
                });

            return badEntries;
        }
    }
}
