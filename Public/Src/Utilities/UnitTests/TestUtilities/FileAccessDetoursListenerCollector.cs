// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Native.IO;
using BuildXL.Processes;
using BuildXL.Utilities;
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
        public override void HandleFileAccess(long pipId, string pipDescription, ReportedFileOperation operation, RequestedAccess requestedAccess, FileAccessStatus status, bool explicitlyReported, uint processId, uint error, DesiredAccess desiredAccess, ShareMode shareMode, CreationDisposition creationDisposition, FlagsAndAttributes flagsAndAttributes, string path, string processArgs)
        {
            if (AbsolutePath.TryCreate(m_pathTable, path, out AbsolutePath absolutePath))
            {
                m_fileAccessPaths.Add(absolutePath);
            }

            m_allFileAccessPaths.Add(path);
        }

        /// <inheritdoc/>
        public override void HandleDebugMessage(long pipId, string pipDescription, string debugMessage)
        {
        }

        /// <inheritdoc/>
        public override void HandleProcessData(long pipId, string pipDescription, string processName, uint processId, DateTime creationDateTime, DateTime exitDateTime, TimeSpan kernelTime, TimeSpan userTime, uint exitCode, IOCounters ioCounters, uint parentProcessId)
        {
        }

        /// <inheritdoc/>
        public override void HandleProcessDetouringStatus(ulong processId, uint reportStatus, string processName, string startApplicationName, string startCommandLine, bool needsInjection, ulong hJob, bool disableDetours, uint creationFlags, bool detoured, uint error, uint createProcessStatusReturn)
        {
        }
    }
}
