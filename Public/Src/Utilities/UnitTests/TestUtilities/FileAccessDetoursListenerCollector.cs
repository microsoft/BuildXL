// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Listens to file accesses reported by detours and collects them
    /// </summary>
    public sealed class FileAccessDetoursListenerCollector : IDetoursEventListener
    {
        private readonly PathTable m_pathTable;
        private readonly ConcurrentBigSet<AbsolutePath> m_fileAccessPaths = new ConcurrentBigSet<AbsolutePath>();
        private readonly ConcurrentBigSet<string> m_allFileAccessPaths = new ConcurrentBigSet<string>();

        /// <summary>
        /// All reported paths that could be parsed as absolute paths
        /// </summary>
        public IReadOnlyCollection<AbsolutePath> GetFileAccessPaths() => m_fileAccessPaths.UnsafeGetList();

        /// <summary>
        /// All file access paths, as they came (raw) from detours
        /// </summary>
        public IReadOnlyCollection<string> GetAllFileAccessPaths() => m_allFileAccessPaths.UnsafeGetList();

        /// <nodoc/>
        public FileAccessDetoursListenerCollector(PathTable pathTable)
        {
            SetMessageHandlingFlags(MessageHandlingFlags.FileAccessNotify | MessageHandlingFlags.FileAccessCollect | MessageHandlingFlags.ProcessDataCollect | MessageHandlingFlags.ProcessDetoursStatusCollect);
            m_pathTable = pathTable;
        }

        /// <inheritdoc/>
        public override void HandleFileAccess(FileAccessData fileAccessData)
        {
            if (AbsolutePath.TryCreate(m_pathTable, fileAccessData.Path, out AbsolutePath absolutePath))
            {
                m_fileAccessPaths.Add(absolutePath);
            }

            m_allFileAccessPaths.Add(fileAccessData.Path);
        }

        /// <inheritdoc/>
        public override void HandleDebugMessage(DebugData debugData)
        {
        }

        /// <inheritdoc/>
        public override void HandleProcessData(ProcessData processData)
        {
        }

        /// <inheritdoc/>
        public override void HandleProcessDetouringStatus(ProcessDetouringStatusData processDetouringStatusData)
        {
        }
    }
}
