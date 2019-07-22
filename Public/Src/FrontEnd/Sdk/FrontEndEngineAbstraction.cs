// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.FrontEnd.Sdk.FileSystem;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// This class abstracts the engine from the frontends
    /// </summary>
    public abstract class FrontEndEngineAbstraction
    {
        // 2x the processor count is the best static guess which won't slow down parsing when disk access is
        // fast. Spinning disks benefit from higher concurrency if the OS filesystem cache is cold.
        //
        // If it is known that a machine has an SSD or the filesystem cache is hot, the optimal setting is
        // the number of physical processors. This gets reset once it is known if the disk has a seek penalty.
        private int m_parserConcurrency = Environment.ProcessorCount * 2;

        /// <summary>
        /// Concurrency to use when parsing spec files
        /// </summary>
        public int ParserConcurrency
        {
            get
            {
                return m_parserConcurrency;
            }

            protected set
            {
                Contract.Requires(value > 0);
                m_parserConcurrency = value;
            }
        }

        /// <summary>
        /// The layout configuration for this build.
        /// </summary>
        public ILayoutConfiguration Layout { get; protected set; }

        /// <summary>
        /// Attempts to place content at the specified path. The content should have previously been stored or loaded into <paramref name="cache"/>.
        /// If not, this operation may fail. This operation may also fail due to I/O-related issues, such as lack of permissions to the destination.
        /// Note that the containing directory for <paramref name="path"/> is assumed to be created already.
        /// The file is added to the file content table, and may be tracked by a tracker.
        /// </summary>
        public abstract Task<Possible<ContentMaterializationResult, Failure>> TryMaterializeContentAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            ContentHash contentHash,
            bool trackPath = true,
            bool recordPathInFileContentTable = true);

        /// <summary>
        /// Whether the directories are enumerated during initializing resolvers or evaluating the spec files
        /// </summary>
        public bool IsAnyDirectoryEnumerated { get; protected set; }

        /// <summary>
        /// Allows frontends to read in files
        /// </summary>
        /// <remarks>
        /// This takes care of tracking and invalidation.
        /// </remarks>
        public abstract bool TryGetFrontEndFile(AbsolutePath path, string frontEnd, out Stream stream);

        /// <summary>
        /// Reads file in a performant manner.
        /// </summary>
        public abstract Task<Possible<FileContent, RecoverableExceptionFailure>> GetFileContentAsync(AbsolutePath path);

        /// <summary>
        /// Determines whether the specified file exists
        /// </summary>
        public abstract bool FileExists(AbsolutePath path);

        /// <summary>
        /// Determines whether the specified file exists and is a directory
        /// </summary>
        public abstract bool DirectoryExists(AbsolutePath path);

        /// <summary>
        /// Records usage of a frontend file.
        /// </summary>
        public abstract void RecordFrontEndFile(AbsolutePath path, string frontEnd);

        /// <summary>
        /// Tracks the directory via InputTracker (should be called whenever a directory is enumerated for DScript), given already enumerated members.
        /// </summary>
        public abstract void TrackDirectory(string directoryPath, IReadOnlyList<(string, FileAttributes)> members);

        /// <summary>
        /// Tracks the directory via InputTracker (should be called whenever a directory is enumerated for DScript)
        /// </summary>
        public abstract void TrackDirectory(string directoryPath);
        
        /// <summary>
        /// Gets the global build parameter.
        /// </summary>
        public abstract bool TryGetBuildParameter(string name, string frontEnd, out string value);

        /// <summary>
        /// Returns the list of mount names available in the current package
        /// </summary>
        public abstract IEnumerable<string> GetMountNames(string frontEnd, ModuleId moduleId);

        /// <summary>
        /// Gets the mount from the engine for the given moduleId
        /// </summary>
        public abstract TryGetMountResult TryGetMount(string name, string frontEnd, ModuleId moduleId, out IMount mount);

        /// <summary>
        /// After config parsing we'll have a list of build parameters that are white-listed.
        /// This method informs the engine of this.
        /// </summary>
        public abstract void RestrictBuildParameters(IEnumerable<string> buildParameterNames);

        /// <summary>
        /// Finishes tracking build parameters at the end of evaluation phase and logs the environment variables impacting the build
        /// </summary>
        public abstract void FinishTrackingBuildParameters();
        
        /// <summary>
        /// Returns files that did not changed since the previous run, or <code>null</code> if no such information is available.
        /// </summary>
        [CanBeNull]
        public abstract IEnumerable<string> GetUnchangedFiles();

        /// <summary>
        /// Returns files that changed since the previous run, or <code>null</code> if no such information is available.
        /// </summary>
        [CanBeNull]
        public abstract IEnumerable<string> GetChangedFiles();

        /// <summary>
        /// Releases memory occumpied by the spec cache.
        /// </summary>
        public virtual void ReleaseSpecCacheMemory()
        {
        }

        /// <summary>
        /// Records the environment variables and enumerated directories in the DScript config file
        /// </summary>
        /// <remarks>
        /// This is only called for DScript front end but it is a temporary fix. We will get rid of it soon.
        /// </remarks>
        public void RecordConfigEvaluation(IReadOnlyList<string> envVariables, ConcurrentDictionary<string, IReadOnlyList<(string, FileAttributes)>> dirs, string frontend)
        {
            Contract.Requires(frontend.Equals("DScript"));

            foreach (var name in envVariables)
            {
                string value;
                TryGetBuildParameter(name, frontend, out value);
            }

            Parallel.ForEach(dirs, d => TrackDirectory(d.Key, d.Value));
        }

        /// <summary>
        /// Gets the update and delay time for status timers for the current logging configuration
        /// </summary>
        public int GetTimerUpdatePeriod { get; protected set; }

        /// <summary>
        /// Returns the hash of a file in an efficient way
        /// </summary>
        public abstract Task<ContentHash> GetFileContentHashAsync(string path, bool trackFile = true, HashType hashType = HashType.Unknown);

        /// <summary>
        /// Whether the engine state (path, string, symbol tables and pip graph) have been reloaded
        /// </summary>
        public abstract bool IsEngineStatePartiallyReloaded();

        /// <summary>
        /// Enumerates file system entries under the given path
        /// </summary>
        public abstract IEnumerable<AbsolutePath> EnumerateEntries(AbsolutePath path, string pattern, bool recursive, bool directories);

        /// <summary>
        /// Enumerates files under the given path
        /// </summary>
        public IEnumerable<AbsolutePath> EnumerateFiles(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            return EnumerateEntries(path, pattern, recursive, directories: false);
        }

        /// <summary>
        /// Enumerates directories under the given path
        /// </summary>
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, string pattern = "*", bool recursive = false)
        {
            return EnumerateEntries(path, pattern, recursive, directories: true);
        }

        /// <summary>
        /// A simple IO based file system entry enumeration helper 
        /// </summary>
        protected static IEnumerable<AbsolutePath> EnumerateEntriesHelper(PathTable pathTable, AbsolutePath path, string pattern, bool recursive, bool directories, IFileSystem fileSystem)
        {
            var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            return directories
                ? fileSystem.EnumerateDirectories(path, pattern, recursive)
                : fileSystem.EnumerateFiles(path, pattern, recursive);
        }
    }
}
