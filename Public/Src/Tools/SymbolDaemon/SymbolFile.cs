// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Utilities;
using Microsoft.VisualStudio.Services.Symbol.Common;
using Tool.ServicePipDaemon;

namespace Tool.SymbolDaemon
{
    /// <summary>
    /// File and its symbol data.
    /// </summary>
    public sealed class SymbolFile
    {
        /// <summary>
        /// Prefix for the error message of the exception that gets thrown when a materialized file is a symlink.
        /// </summary>
        private const string MaterializationResultIsSymlinkErrorPrefix = "File materialization succeeded, but file found on disk is a symlink: ";

        private readonly Func<string, bool> m_symlinkTester;
        private readonly FileArtifact m_file;
        private readonly Client m_bxlClient;

        /// <summary>
        /// List of the symbol data from the file. 
        /// </summary>
        /// <remarks>
        /// Enforcing that the DebugEntries are in fact from the current file is somewhat expensive,
        /// so there is an expectation this class won't be constructed using entries from another file.
        /// 
        /// <see cref="ISymbolIndexer.GetDebugEntries"/> can create entries, hash the file,
        /// and set <see cref="IDebugEntryData.BlobIdentifier"/> to a proper value, but this would mean
        /// that we are hashing the same file twice.            
        /// </remarks>
        private List<DebugEntryData> m_debugEntries;

        /// <summary>
        /// File content hash
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// File path (no guarantee that the file is present on disk)
        /// </summary>
        public readonly string FullFilePath;

        /// <summary>
        /// Symbol entries
        /// </summary>   
        /// <remarks>
        /// no entries => file has no symbol data
        /// null => file has not been indexed yet
        /// </remarks>
        public IReadOnlyList<DebugEntryData> DebugEntries => m_debugEntries;

        /// <summary>
        /// Whether the file has been indexed
        /// </summary>
        public bool IsIndexed => m_debugEntries != null;

        /// <nodoc/>
        public SymbolFile(Client bxlClient, string filePath, string fileId, ContentHash hash, IEnumerable<DebugEntryData> debugEntries = null)
            : this(Statics.IsSymLinkOrMountPoint, bxlClient, filePath, fileId, hash, debugEntries)
        {
        }

        internal SymbolFile(
            Func<string, bool> symlinkTester,
            Client bxlClient,
            string filePath,
            string fileId,
            ContentHash hash,
            IEnumerable<DebugEntryData> debugEntries)
        {
            Contract.Requires(symlinkTester != null);
            Contract.Requires(bxlClient != null);
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(!string.IsNullOrEmpty(fileId));
            Contract.Requires(debugEntries == null || debugEntries.All(de => de.BlobIdentifier == null));

            // It's not clear whether the symbol endpoint can play nicely with dedup hashes, so locking it down to VSO0 for now.
            Contract.Requires(hash.HashType == HashType.Vso0, "support only VSO0 hashes (for now)");

            m_symlinkTester = symlinkTester;
            m_bxlClient = bxlClient;
            FullFilePath = Path.GetFullPath(filePath);
            m_file = FileId.Parse(fileId);
            Hash = hash;

            if (debugEntries != null)
            {
                var blobIdentifier = new Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier(Hash.ToHashByteArray());

                var entries = new List<DebugEntryData>(debugEntries);
                entries.ForEach(entry => entry.BlobIdentifier = blobIdentifier);
                m_debugEntries = entries;
            }
        }

        /// <nodoc/>    
        public void SetDebugEntries(List<DebugEntryData> entries)
        {
            Contract.Requires(entries != null);

            // check that either all entries are missing the blobId, or all the entries have the same blobId and that blobId matches this file
            var blobIdentifier = new Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier(Hash.ToHashByteArray());
            Contract.Assert(entries.All(e => e.BlobIdentifier == null) || entries.All(e => e.BlobIdentifier == blobIdentifier));

            // ensure that BlobIdentifier is not null
            // here we 'trust' that the debug entries are from the current symbol file
            entries.ForEach(entry => entry.BlobIdentifier = blobIdentifier);
            m_debugEntries = entries;
        }

        /// <summary>
        /// FileInfo is not already computed, sends an IPC request to BuildXL to materialize the file;
        /// if the request succeeds, returns a <see cref="FileInfo"/> corresponding to that file,
        /// otherwise throws a <see cref="DaemonException"/>.
        /// </summary>
        public async Task<FileInfo> EnsureMaterializedAsync()
        {
            Possible<bool> maybeResult = await m_bxlClient.MaterializeFile(m_file, FullFilePath);

            if (!maybeResult.Succeeded)
            {
                throw new DaemonException(maybeResult.Failure.Describe());
            }

            if (!maybeResult.Result)
            {
                throw new DaemonException("File materialization failed");
            }

            if (!File.Exists(FullFilePath))
            {
                throw new DaemonException("File materialization succeeded, but file is not found on disk: " + FullFilePath);
            }

            if (m_symlinkTester(FullFilePath))
            {
                throw new DaemonException(MaterializationResultIsSymlinkErrorPrefix + FullFilePath);
            }

            return new FileInfo(FullFilePath);
        }

        /// <nodoc/>
        public override string ToString()
        {
            return FileId.ToString(m_file);
        }
    }
}
