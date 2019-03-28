// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Stores information about a file that was accessed during the build.
    /// </summary>
    public sealed class FileDescriptor
    {
        #region Private classes
        private sealed class FileContentData
        {
            internal readonly ContentHash ContentHash;
            internal readonly long ContentLength;

            internal FileContentData(ContentHash contentHash, long contentLength)
            {
                ContentHash = contentHash;
                ContentLength = contentLength;
            }
        }
        #endregion

        #region Private properties
        private PathTable m_pathTable;
        private AbsolutePath m_fileNameId;
        private List<FileContentData> m_fileContentData;
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal collection of pips that produce this file
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> ProducingPipsHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Internal collection of pips that depend on this file
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> DependentPipsHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Internal collection of pips that execute this file.
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> PipsThatExecuteThisFileHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Internal collection of directories that contain this file.
        /// </summary>
        internal readonly ConcurrentHashSet<DirectoryDescriptor> DirectoriesThatContainThisFileHashset = null;
        #endregion

        #region Public properties

        /// <summary>
        /// Constant value used to signal that the content length is unknown.
        /// </summary>
        public const long UnknownContentLength = -1;

        /// <summary>
        /// The name of the file with full path.
        /// </summary>
        public string FileName => m_pathTable.AbsolutePathToString(m_fileNameId);

        /// <summary>
        /// The content length in bytes, or -1 when the file length is not available.
        /// </summary>
        public long ContentLength => m_fileContentData[RewriteCount].ContentLength;

        /// <summary>
        /// The hash value of the file's content.
        /// </summary>
        public ContentHash ContentHash => m_fileContentData[RewriteCount].ContentHash;

        /// <summary>
        /// The number of times the file has been written during the build.
        /// </summary>
        /// <remarks>
        /// 0 means the files is source file
        /// >0 means that file is an output file
        /// </remarks>
        public int RewriteCount { get; private set; }

        /// <summary>
        /// Signals that the file has been accessed by some pips during the build and these pips did not declare the file as a source file.
        /// </summary>
        public bool IsObservedInputFile { get; internal set; }

        /// <summary>
        /// Signals that the file has been probed and it did not exist
        /// </summary>
        public bool WasFileProbed { get; internal set; }

        /// <summary>
        /// Signals that this file has been built during the build(it was not deployed from cache, was not up to date and did not fail to build).
        /// </summary>
        public bool WasItProducedDuringTheBuild
        {
            get
            {
                return IsOutputFile && ProducingPipsHashset.Where(p => p.WasItBuilt).Any();
            }
        }

        /// <summary>
        /// Signals that the file is a source (input) file for at least one pip.
        /// </summary>
        public bool WasItDeployedFromCache
        {
            get
            {
                return IsOutputFile && ProducingPipsHashset.Where(p => p.WasDeployedFromCache).Any();
            }
        }

        /// <summary>
        /// Signals that the file is a source (input) file for at least one pip.
        /// </summary>
        public bool IsSourceFile
        {
            get
            {
                return DependentPipsHashset.Count > 0;
            }
        }

        /// <summary>
        /// Signals that the file is an output for a pip.
        /// </summary>
        public bool IsOutputFile
        {
            get
            {
                return ProducingPipsHashset.Count > 0;
            }
        }

        /// <summary>
        /// Signals that the file is a tool that is being executed by one or more pips.
        /// </summary>
        public bool IsTool
        {
            get
            {
                return PipsThatExecuteThisFileHashset.Count > 0;
            }
        }

        /// <summary>
        /// The pips that produce this file.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> ProducingPips { get { return ProducingPipsHashset; } }

        /// <summary>
        /// The pips that depend on this file.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> DependentPips { get { return DependentPipsHashset; } }

        /// <summary>
        /// The pips that execute this file.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> PipsThatExecuteThisFile { get { return PipsThatExecuteThisFileHashset; } }

        /// <summary>
        /// The directories that contain this file.
        /// </summary>
        public IReadOnlyCollection<DirectoryDescriptor> DirectoriesThatContainThisFile { get { return DirectoriesThatContainThisFileHashset; } }

        /// <summary>
        /// Unique file identifier
        /// </summary>
        public int FileId => m_fileNameId.RawValue;
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="fileNameId">AbsolutePath value of the file</param>
        /// <param name="pathTable">Path table used to convert the absolute path into a string</param>
        /// <param name="directoriesThatContainThisFileHashset">If not null, this indicates that the load options are such that the DirectoriesThatContainThisFileHashset property will not be used</param>
        internal FileDescriptor(AbsolutePath fileNameId, PathTable pathTable, ConcurrentHashSet<DirectoryDescriptor> directoriesThatContainThisFileHashset)
        {
            Contract.Requires(fileNameId.IsValid);
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_fileNameId = fileNameId;

            m_fileContentData = new List<FileContentData>();

            // RewriteCount has an initial value of zero so we must have default data at index zero of this array
            m_fileContentData.Add(new FileContentData(ContentHashingUtilities.ZeroHash, UnknownContentLength));

            IsObservedInputFile = false;
            WasFileProbed = false;

            if (directoriesThatContainThisFileHashset != null)
            {
                DirectoriesThatContainThisFileHashset = directoriesThatContainThisFileHashset;
            }
            else
            {
                DirectoriesThatContainThisFileHashset = new ConcurrentHashSet<DirectoryDescriptor>();
            }
        }

        /// <summary>
        /// Stores the provided rewrite data
        /// </summary>
        /// <param name="rewriteCount">Rewrite count of file</param>
        /// <param name="contentHash">Hash of file</param>
        /// <param name="contentLength">Optional, length of file</param>
        internal void AddFileRewriteData(int rewriteCount, ContentHash contentHash, long contentLength = UnknownContentLength)
        {
            // Negative rewrite count is nonsensical
            Contract.Requires(rewriteCount >= 0);

            // Check if the list is already large enough to insert the data
            if (rewriteCount < m_fileContentData.Count)
            {
                if (m_fileContentData[rewriteCount] == null)
                {
                    // Put new data into list
                    m_fileContentData[rewriteCount] = new FileContentData(contentHash, contentLength);
                }
                else
                {
                    // We have seen data before for this rewrite count value so only use new data if it is 'better' than data we have already seen
                    // Better here means that it has a known content length. The hash value shouldn't ever be different but the content length isn't always known
                    if (contentLength != UnknownContentLength && m_fileContentData[rewriteCount].ContentLength == UnknownContentLength)
                    {
                        // New data is better than old data so update to new data
                        m_fileContentData[rewriteCount] = new FileContentData(contentHash, contentLength);
                    }
                }
            }
            else
            {
                // Grow the list to fit the new data and add it
                m_fileContentData.AddRange(Enumerable.Repeat((FileContentData)null, rewriteCount - m_fileContentData.Count + 1));
                m_fileContentData[rewriteCount] = new FileContentData(contentHash, contentLength);
            }

            // We keep track of the largest rewrite count for each file and assume that the data for the file corresponds to the largest rewrite count data
            RewriteCount = Math.Max(RewriteCount, rewriteCount);
        }
        #endregion

        public override string ToString()
        {
            return FileName;
        }
    }
}
