// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Ipc.ExternalApi;
using BuildXL.Storage;
using BuildXL.Utilities;
using Tool.ServicePipDaemon;

namespace Tool.DropDaemon
{
    /// <summary>
    ///     Drop item tied to a file provided by BuildXL.
    /// </summary>
    public sealed class DropItemForBuildXLFile : DropItemForFile
    {
        /// <summary>Prefix for the error message of the exception that gets thrown when a materialized file is a symlink.</summary>
        internal const string MaterializationResultIsSymlinkErrorPrefix = "File materialization succeeded, but file found on disk is a symlink: ";

        /// <summary>
        /// File content hash
        /// </summary>
        public readonly ContentHash Hash;
        
        private readonly Func<string, bool> m_symlinkTester;
        private readonly FileArtifact m_file;
        private readonly Client m_client;
        
        /// <summary>
        /// Whether it is an output file or not 
        /// </summary>
        public bool IsOutputFile => m_file.IsOutputFile;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        private readonly bool m_chunkDedup;

        /// <nodoc/>
        public DropItemForBuildXLFile(Client client, string filePath, string fileId, bool chunkDedup, FileContentInfo fileContentInfo, string relativeDropPath = null)
            : this(Statics.IsSymLinkOrMountPoint, client, filePath, fileId, chunkDedup, fileContentInfo, relativeDropPath)
        {
        }

        internal DropItemForBuildXLFile(
            Func<string, bool> symlinkTester,
            Client client,
            string filePath,
            string fileId,
            bool chunkDedup,
            FileContentInfo fileContentInfo,
            string relativeDropPath = null)
            : base(filePath, relativeDropPath, fileContentInfo)
        {
            Contract.Requires(symlinkTester != null);
            Contract.Requires(client != null);
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(!string.IsNullOrEmpty(fileId));

            m_symlinkTester = symlinkTester;
            m_file = FileId.Parse(fileId);
            m_client = client;
            m_chunkDedup = chunkDedup;
            Hash = fileContentInfo.Hash;
        }

        /// <summary>
        ///     FileInfo is not already computed, sends an IPC request to BuildXL to materialize the file;
        ///     if the request succeeds, returns a <see cref="FileInfo"/> corresponding to that file,
        ///     otherwise throws a <see cref="DaemonException"/>.
        /// </summary>
        public override async Task<FileInfo> EnsureMaterialized()
        {
            Possible<bool> maybeResult = await m_client.MaterializeFile(m_file, FullFilePath);
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

#if DEBUG
            // If a BlobIdentifier was explicitly provided, double check
            // that it is the same as the one calculated from the file on disk.
            if (BlobIdentifier != null)
            {
                await ComputeAndDoubleCheckBlobIdentifierAsync(BlobIdentifier, FullFilePath, FileLength, m_chunkDedup, phase: "MaterializeFile", cancellationToken: CancellationToken.None);
            }
#endif

            return new FileInfo(FullFilePath);
        }

        /// <nodoc/>
        public override string ToString()
        {
            return FileId.ToString(m_file);
        }
    }
}
