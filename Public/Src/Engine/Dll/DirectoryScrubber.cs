// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Class for scrubbing extraneous files in directories.
    /// </summary>
    public sealed class DirectoryScrubber
    {
        private const string Category = "Scrubbing";
        private readonly CancellationToken m_cancellationToken;
        private readonly LoggingContext m_loggingContext;
        private readonly ILoggingConfiguration m_loggingConfiguration;
        private readonly int m_maxDegreeParallelism;
        private readonly ITempCleaner m_tempDirectoryCleaner;

        /// <summary>
        /// Creates an instance of <see cref="DirectoryScrubber"/>.
        /// </summary>
        public DirectoryScrubber(
            CancellationToken cancellationToken,
            LoggingContext loggingContext,
            ILoggingConfiguration loggingConfiguration,
            int maxDegreeParallelism,
            ITempCleaner tempDirectoryCleaner = null)
        {
            m_cancellationToken = cancellationToken;
            m_loggingContext = loggingContext;
            m_loggingConfiguration = loggingConfiguration;
            m_maxDegreeParallelism = maxDegreeParallelism;
            m_tempDirectoryCleaner = tempDirectoryCleaner;
        }

        /// <summary>
        /// Collapses a set of paths by removing paths that are nested within other paths.
        /// </summary>
        /// <remarks>
        /// This allows the scrubber to operate on the outer most paths and avoid duplicating work from potentially
        /// starting additional directory traversal from nested paths.
        /// </remarks>
        public static IEnumerable<string> CollapsePaths(IEnumerable<string> paths)
        {
            paths = paths.Select(path => (path.Length > 0 && path[path.Length - 1] == Path.DirectorySeparatorChar) ? path : path + @"\").OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            string lastPath = null;
            foreach(var path in paths)
            {
                if (lastPath == null || !path.StartsWith(lastPath, StringComparison.OrdinalIgnoreCase))
                {
                    // returned value should not have \ on end so that it matched with blockedpaths.
                    yield return path.Substring(0, path.Length - 1);
                    lastPath = path;
                }
            }
        }

        /// <summary>
        /// Validates directory paths to scrub.
        /// </summary>
        /// <remarks>
        /// A directory path is valid to scrub if it is under a scrubbable mount.
        /// </remarks>
        private bool ValidateDirectory(MountPathExpander mountPathExpander, string directory, out SemanticPathInfo foundSemanticPathInfo)
        {
            foundSemanticPathInfo = SemanticPathInfo.Invalid;
            return mountPathExpander == null ||
                   (foundSemanticPathInfo = mountPathExpander.GetSemanticPathInfo(directory)).IsScrubbable;
        }

        /// <summary>
        /// Scrubs extraneous files and directories.
        /// </summary>
        /// <param name="isPathInBuild">
        /// A function that returns true if a given path is in the build.
        /// Basically, a path is in a build if it points to an artifact in the pip graph, i.e., path to a source file, or
        /// an output file, or a sealed directory. Paths that are in the build should not be deleted.
        /// </param>
        /// <param name="pathsToScrub">
        /// Contains a list of paths, including their child paths, that need to be
        /// scrubbed. Basically, the directory scrubber enumerates those paths recursively for removing extraneous files
        /// or directories in those list.
        /// </param>
        /// <param name="blockedPaths">
        /// Stop the above enumeration performed by directory scrubber. All file/directories underneath a blocked path should not be removed.
        /// </param>
        /// <param name="nonDeletableRootDirectories">
        /// Contains list of directory paths that can never be deleted, however,
        /// the contents of the directory can be scrubbed. For example, mount roots should not be deleted.
        /// </param>
        /// <param name="mountPathExpander">
        /// Optional mount path expander.  When used, its roots are treated as non-deletable.
        /// </param>
        /// <param name="logRemovedFiles">
        /// Optional flag for logging removed files.
        /// </param>
        public bool RemoveExtraneousFilesAndDirectories(
            Func<string, bool> isPathInBuild,
            IEnumerable<string> pathsToScrub,
            IEnumerable<string> blockedPaths,
            IEnumerable<string> nonDeletableRootDirectories,
            MountPathExpander mountPathExpander = null,
            bool logRemovedFiles = true)
        {
            var finalPathsToScrub = CollapsePaths(pathsToScrub).ToList();
            var finalBlockedPaths = new HashSet<string>(blockedPaths, StringComparer.OrdinalIgnoreCase);
            var finalNonDeletableRootDirectories = new HashSet<string>(nonDeletableRootDirectories, StringComparer.OrdinalIgnoreCase);
            if (mountPathExpander != null)
            {
                finalNonDeletableRootDirectories.UnionWith(mountPathExpander.GetAllRoots().Select(p => p.ToString(mountPathExpander.PathTable)));
            }

            return RemoveExtraneousFilesAndDirectories(isPathInBuild, finalPathsToScrub, finalBlockedPaths, finalNonDeletableRootDirectories, mountPathExpander, logRemovedFiles);
        }

        private bool RemoveExtraneousFilesAndDirectories(
            Func<string, bool> isPathInBuild,
            List<string> pathsToScrub,
            HashSet<string> blockedPaths,
            HashSet<string> nonDeletableRootDirectories,
            MountPathExpander mountPathExpander,
            bool logRemovedFiles)
        { 
            int directoriesEncountered = 0;
            int filesEncountered = 0;
            int filesRemoved = 0;
            int directoriesRemovedRecursively = 0;

            using (var pm = PerformanceMeasurement.Start(
                m_loggingContext,
                Category,
                // The start of the scrubbing is logged before calling this function, since there are two sources of scrubbing (regular scrubbing and shared opaque scrubbing)
                // with particular messages
                (_ => {}),
                loggingContext =>
                    {
                        Tracing.Logger.Log.ScrubbingFinished(loggingContext, directoriesEncountered, filesEncountered, filesRemoved, directoriesRemovedRecursively);
                        Logger.Log.BulkStatistic(
                            loggingContext,
                            new Dictionary<string, long>
                            {
                                [I($"{Category}.DirectoriesEncountered")] = directoriesEncountered,
                                [I($"{Category}.FilesEncountered")] = filesEncountered,
                                [I($"{Category}.FilesRemoved")] = filesRemoved,
                                [I($"{Category}.DirectoriesRemovedRecursively")] = directoriesRemovedRecursively,
                            });
                    }))
            using (var timer = new Timer(
                o =>
                {
                    // We don't have a good proxy for how much scrubbing is left. Instead we use the file counters to at least show progress
                    Tracing.Logger.Log.ScrubbingStatus(m_loggingContext, filesEncountered);
                },
                null,
                dueTime: BuildXLEngine.GetTimerUpdatePeriodInMs(m_loggingConfiguration),
                period: BuildXLEngine.GetTimerUpdatePeriodInMs(m_loggingConfiguration)))
            {
                var deletableDirectoryCandidates = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var nondeletableDirectories = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var directoriesToEnumerate = new BlockingCollection<string>();

                foreach (var path in pathsToScrub)
                {
                    SemanticPathInfo foundSemanticPathInfo;

                    if (blockedPaths.Contains(path))
                    {
                        continue;
                    }

                    if (ValidateDirectory(mountPathExpander, path, out foundSemanticPathInfo))
                    {
                        if (!isPathInBuild(path))
                        {
                            directoriesToEnumerate.Add(path);
                        }
                        else
                        {
                            nondeletableDirectories.TryAdd(path, true);
                        }
                    }
                    else
                    {
                        string mountName = "Invalid";
                        string mountPath = "Invalid";

                        if (mountPathExpander != null && foundSemanticPathInfo.IsValid)
                        {
                            mountName = foundSemanticPathInfo.RootName.ToString(mountPathExpander.PathTable.StringTable);
                            mountPath = foundSemanticPathInfo.Root.ToString(mountPathExpander.PathTable);
                        }

                        Tracing.Logger.Log.ScrubbingFailedBecauseDirectoryIsNotScrubbable(pm.LoggingContext, path, mountName, mountPath);
                    }
                }

                var cleaningThreads = new Thread[m_maxDegreeParallelism];
                int pending = directoriesToEnumerate.Count;

                if (directoriesToEnumerate.Count == 0)
                {
                    directoriesToEnumerate.CompleteAdding();
                }

                for (int i = 0; i < m_maxDegreeParallelism; i++)
                {
                    var t = new Thread(() =>
                    {
                        while (!directoriesToEnumerate.IsCompleted && !m_cancellationToken.IsCancellationRequested)
                        {
                            string currentDirectory;
                            if (directoriesToEnumerate.TryTake(out currentDirectory, Timeout.Infinite))
                            {
                                Interlocked.Increment(ref directoriesEncountered);
                                bool shouldDeleteCurrentDirectory = true;

                                var result = FileUtilities.EnumerateDirectoryEntries(
                                    currentDirectory,
                                    false,
                                    (dir, fileName, attributes) =>
                                    {
                                        string fullPath = Path.Combine(dir, fileName);

                                        // Skip specifically blocked paths.
                                        if (blockedPaths.Contains(fullPath))
                                        {
                                            shouldDeleteCurrentDirectory = false;
                                            return;
                                        }

                                        // important to not follow directory symlinks because that can cause 
                                        // re-enumerating and scrubbing the same physical folder multiple times
                                        if (FileUtilities.IsDirectoryNoFollow(attributes))
                                        {
                                            if (nondeletableDirectories.ContainsKey(fullPath))
                                            {
                                                shouldDeleteCurrentDirectory = false;
                                            }

                                            if (!isPathInBuild(fullPath))
                                            {
                                                // Current directory is not in the build, then recurse to its members.
                                                Interlocked.Increment(ref pending);
                                                directoriesToEnumerate.Add(fullPath);

                                                if (!nonDeletableRootDirectories.Contains(fullPath))
                                                {
                                                    // Current directory can be deleted, then it is a candidate to be deleted.
                                                    deletableDirectoryCandidates.TryAdd(fullPath, true);
                                                }
                                                else
                                                {
                                                    // Current directory can't be deleted (e.g., the root of a mount), then don't delete it.
                                                    // However, note that we recurse to its members to find all extraneous directories and files.
                                                    shouldDeleteCurrentDirectory = false;
                                                }
                                            }
                                            else
                                            {
                                                // Current directory is in the build, i.e., directory is an output directory.
                                                // Stop recursive directory traversal because none of its members should be deleted.
                                                shouldDeleteCurrentDirectory = false;
                                            }
                                        }
                                        else
                                        {
                                            Interlocked.Increment(ref filesEncountered);

                                            if (!isPathInBuild(fullPath))
                                            {
                                                // File is not in the build, delete it.
                                                if (TryDeleteFile(pm.LoggingContext, fullPath, logRemovedFiles))
                                                {
                                                    Interlocked.Increment(ref filesRemoved);
                                                }
                                            }
                                            else
                                            {
                                                // File is in the build, then don't delete it, but mark the current directory that
                                                // it should not be deleted.
                                                shouldDeleteCurrentDirectory = false;
                                            }
                                        }
                                    });

                                if (!result.Succeeded)
                                {
                                    // Different trace levels based on result.
                                    if (result.Status != EnumerateDirectoryStatus.SearchDirectoryNotFound)
                                    {
                                        Tracing.Logger.Log.ScrubbingFailedToEnumerateDirectory(
                                            pm.LoggingContext,
                                            currentDirectory,
                                            result.Status.ToString());
                                    }
                                }

                                if (!shouldDeleteCurrentDirectory)
                                {
                                    // If directory should not be deleted, then all of its parents should not be deleted.
                                    int index;
                                    string preservedDirectory = currentDirectory;
                                    bool added;

                                    do
                                    {
                                        added = nondeletableDirectories.TryAdd(preservedDirectory, true);
                                    }
                                    while (added
                                           && (index = preservedDirectory.LastIndexOf(Path.DirectorySeparatorChar)) != -1
                                           && !string.IsNullOrEmpty(preservedDirectory = preservedDirectory.Substring(0, index)));
                                }

                                Interlocked.Decrement(ref pending);
                            }

                            if (Volatile.Read(ref pending) == 0)
                            {
                                directoriesToEnumerate.CompleteAdding();
                            }
                        }
                    });
                    t.Start();
                    cleaningThreads[i] = t;
                }

                foreach (var t in cleaningThreads)
                {
                    t.Join();
                }

                // Collect all directories that need to be deleted.
                var deleteableDirectories = new HashSet<string>(deletableDirectoryCandidates.Keys, StringComparer.OrdinalIgnoreCase);
                deleteableDirectories.ExceptWith(nondeletableDirectories.Keys);

                // Delete directories by considering only the top-most ones.
                try
                {
                    Parallel.ForEach(
                        CollapsePaths(deleteableDirectories).ToList(),
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = m_maxDegreeParallelism,
                            CancellationToken = m_cancellationToken,
                        },
                        directory =>
                        {
                            try
                            {
                                FileUtilities.DeleteDirectoryContents(directory, deleteRootDirectory: true, tempDirectoryCleaner: m_tempDirectoryCleaner);
                                Interlocked.Increment(ref directoriesRemovedRecursively);
                            }
                            catch (BuildXLException ex)
                            {
                                Tracing.Logger.Log.ScrubbingExternalFileOrDirectoryFailed(
                                    pm.LoggingContext,
                                    directory,
                                    ex.LogEventMessage);
                            }
                        });
                }
                catch (OperationCanceledException) { }
                return true;
            }
        }

        /// <summary>
        /// Deletes a given array files in parallel.
        /// Deletion failures are handled gracefully (by logging a warning).
        /// Returns the number of successfully deleted files.
        /// </summary>
        public int DeleteFiles(string[] filePaths, bool logDeletedFiles = true)
        {
            int numRemoved = 0;
            using (var timer = new Timer(
                _ => Tracing.Logger.Log.ScrubbingProgress(m_loggingContext, "", numRemoved, filePaths.Length),
                null,
                dueTime: BuildXLEngine.GetTimerUpdatePeriodInMs(m_loggingConfiguration),
                period: BuildXLEngine.GetTimerUpdatePeriodInMs(m_loggingConfiguration)))
            {
                filePaths
                    .AsParallel()
                    .WithDegreeOfParallelism(m_maxDegreeParallelism)
                    .WithCancellation(m_cancellationToken)
                    .ForAll(path =>
                    {
                        if (FileUtilities.FileExistsNoFollow(path) && TryDeleteFile(m_loggingContext, path, logDeletedFiles))
                        {
                            Interlocked.Increment(ref numRemoved);
                        }
                    });
                Tracing.Logger.Log.ScrubbingFinished(m_loggingContext, 0, filePaths.Length, numRemoved, 0);
                return numRemoved;
            }
        }

        private bool TryDeleteFile(LoggingContext loggingContext, string path, bool logDeletedFile)
        {
            try
            {
                FileUtilities.DeleteFile(path, waitUntilDeletionFinished: true, tempDirectoryCleaner: m_tempDirectoryCleaner);

                if (logDeletedFile)
                {
                    Tracing.Logger.Log.ScrubbingFile(loggingContext, path);
                }

                return true;
            }
            catch (BuildXLException ex)
            {
                Tracing.Logger.Log.ScrubbingExternalFileOrDirectoryFailed(loggingContext, path, ex.LogEventMessage);
                return false;
            }
        }
    }
}
