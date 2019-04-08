// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Utilities;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// Describes a directory that has been accessed during the build.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix", Justification = "This class should not be called a Collection")]
    public sealed class DirectoryDescriptor
    {
        #region Private properties

        /// <summary>
        /// The AbsolutePath value for the Path
        /// </summary>
        private readonly AbsolutePath m_path;

        /// <summary>
        /// Used to convert an AbsolutePath to a string
        /// </summary>
        private readonly PathTable m_pathTable;
        #endregion

        #region Internal properties

        /// <summary>
        /// Internal collection of pips that produce files in this directory
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> ProducingPipsHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Internal collection of pips that depend on files in this directory
        /// </summary>
        internal readonly ConcurrentHashSet<PipDescriptor> DependentPipsHashset = new ConcurrentHashSet<PipDescriptor>();

        /// <summary>
        /// Internal collection of files that the build references in this directory
        /// </summary>
        internal readonly ConcurrentHashSet<FileDescriptor> FilesHashset = null;

        /// <summary>
        /// Internal dictionary storing pip specific directory hashes
        /// </summary>
        internal readonly ConcurrentDictionary<PipDescriptor, ContentHash> ContentHashDictionary = new ConcurrentDictionary<PipDescriptor, ContentHash>();
        #endregion

        #region Public properties

        /// <summary>
        /// The directory path
        /// </summary>
        public string Path => m_pathTable.AbsolutePathToString(m_path);

        /// <summary>
        /// Pip specific hash values from BuildXL generated based on the content of the directory.
        /// </summary>
        public IReadOnlyDictionary<PipDescriptor, ContentHash> ContentHashes { get { return ContentHashDictionary; } }

        /// <summary>
        /// Signals that the directory has been accessed during the build, but it has not been specified in spec files as a dependency.
        /// </summary>
        public bool IsObservedInput { get; internal set; }

        /// <summary>
        /// Pips that produce files in this directory.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> ProducingPips { get { return ProducingPipsHashset; } }

        /// <summary>
        /// Pips that depend on files from this directory.
        /// </summary>
        public IReadOnlyCollection<PipDescriptor> DependentPips { get { return DependentPipsHashset; } }

        /// <summary>
        /// Files referenced by the build from this directory.
        /// </summary>
        public IReadOnlyCollection<FileDescriptor> Files { get { return FilesHashset; } }
        #endregion

        #region Internal methods

        /// <summary>
        /// Internal constructor
        /// </summary>
        /// <param name="path">The AbsolutePath value of the path that describes the directory</param>
        /// <param name="pathTable">Used to convert an AbsolutePath to a string</param>
        /// <param name="filesHashset">If not null, this indicates that the load options are such that the FileHashSet property will not be used</param>
        internal DirectoryDescriptor(AbsolutePath path, PathTable pathTable, ConcurrentHashSet<FileDescriptor> filesHashset)
        {
            m_path = path;
            m_pathTable = pathTable;
            if (filesHashset != null)
            {
                FilesHashset = filesHashset;
            }
            else
            {
                FilesHashset = new ConcurrentHashSet<FileDescriptor>();
            }
        }
        #endregion

        public override string ToString()
        {
            return Path;
        }
    }
}
