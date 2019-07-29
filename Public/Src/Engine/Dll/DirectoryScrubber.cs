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
    internal sealed class DirectoryScrubber
    {
        private const string Category = "Scrubbing";

        private readonly LoggingContext m_loggingContext;
        private readonly ILoggingConfiguration m_loggingConfiguration;
        private readonly Func<string, bool> m_isPathInBuild;
        private readonly IEnumerable<string> m_pathsToScrub;
        private readonly HashSet<string> m_blockedPaths;
        private readonly int m_maxDegreeParallelism;
        private readonly MountPathExpander m_mountPathExpander;
        private readonly ITempCleaner m_tempDirectoryCleaner;

        // Directories that can be scrubbed but cannot be deleted.
        private readonly HashSet<string> m_nonDeletableRootDirectories;

        /// <summary>
        /// Creates an instance of <see cref="DirectoryScrubber"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="isPathInBuild"/> is a delegate that returns true if a given path is in the build.
        /// Basically, a path is in a build if it points to an artifact in the pip graph, i.e., path to a source file, or
        /// an output file, or a sealed directory. Paths that are in the build should not be deleted.
        /// 
        /// <paramref name="pathsToScrub"/> contains a list of paths, including their child paths, that need to be
        /// scrubbed. Basically, the directory scrubber enumerates those paths recursively for removing extraneous files
        /// or directories in those list.
        /// 
        /// <paramref name="blockedPaths"/> stop the above enumeration performed by directory scrubber. All file/directories
        /// underneath a blocked path should not be removed.
        /// 
        /// <paramref name="nonDeletableRootDirectories"/> contains list of directory paths that can never be deleted, however,
        /// the contents of the directory can be scrubbed. For example, mount roots should not be deleted.
        /// </remarks>
        public DirectoryScrubber(
            LoggingContext loggingContext,
            ILoggingConfiguration loggingConfiguration,
            Func<string, bool> isPathInBuild,
            IEnumerable<string> pathsToScrub,
            IEnumerable<string> blockedPaths,
            IEnumerable<string> nonDeletableRootDirectories,
            MountPathExpander mountPathExpander,
            int maxDegreeParallelism,
            ITempCleaner tempDirectoryCleaner = null)
        {
            m_loggingContext = loggingContext;
            m_loggingConfiguration = loggingConfiguration;
            m_isPathInBuild = isPathInBuild;
            m_pathsToScrub = CollapsePaths(pathsToScrub).ToList();
            m_blockedPaths = new HashSet<string>(blockedPaths, StringComparer.OrdinalIgnoreCase);
            m_mountPathExpander = mountPathExpander;
            m_maxDegreeParallelism = maxDegreeParallelism;
            m_nonDeletableRootDirectories = new HashSet<string>(nonDeletableRootDirectories, StringComparer.OrdinalIgnoreCase);

            if (mountPathExpander != null)
            {
                m_nonDeletableRootDirectories.UnionWith(mountPathExpander.GetAllRoots().Select(p => p.ToString(mountPathExpander.PathTable)));
            }

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
        private bool ValidateDirectory(string directory, out SemanticPathInfo foundSemanticPathInfo)
        {
            foundSemanticPathInfo = SemanticPathInfo.Invalid;
            return m_mountPathExpander == null ||
                   (foundSemanticPathInfo = m_mountPathExpander.GetSemanticPathInfo(directory)).IsScrubbable;
        }

        /// <summary>
        /// Scrubs extraneous files and directories.
        /// </summary>
        public bool RemoveExtraneousFilesAndDirectories(CancellationToken cancellationToken)
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

                foreach (var path in m_pathsToScrub)
                {
                    SemanticPathInfo foundSemanticPathInfo;

                    if (m_blockedPaths.Contains(path))
                    {
                        continue;
                    }

                    if (ValidateDirectory(path, out foundSemanticPathInfo))
                    {
                        if (!m_isPathInBuild(path))
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

                        if (m_mountPathExpander != null && foundSemanticPathInfo.IsValid)
                        {
                            mountName = foundSemanticPathInfo.RootName.ToString(m_mountPathExpander.PathTable.StringTable);
                            mountPath = foundSemanticPathInfo.Root.ToString(m_mountPathExpander.PathTable);
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
                        while (!directoriesToEnumerate.IsCompleted && !cancellationToken.IsCancellationRequested)
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
                                        if (m_blockedPaths.Contains(fullPath))
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

                                            if (!m_isPathInBuild(fullPath))
                                            {
                                                // Current directory is not in the build, then recurse to its members.
                                                Interlocked.Increment(ref pending);
                                                directoriesToEnumerate.Add(fullPath);

                                                if (!m_nonDeletableRootDirectories.Contains(fullPath))
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

                                            if (!m_isPathInBuild(fullPath))
                                            {
                                                // File is not in the build, delete it.
                                                try
                                                {
                                                    FileUtilities.DeleteFile(fullPath, tempDirectoryCleaner: m_tempDirectoryCleaner);
                                                    Interlocked.Increment(ref filesRemoved);

                                                    Tracing.Logger.Log.ScrubbingFile(pm.LoggingContext, fullPath);
                                                }
                                                catch (BuildXLException ex)
                                                {
                                                    Tracing.Logger.Log.ScrubbingExternalFileOrDirectoryFailed(
                                                        pm.LoggingContext,
                                                        fullPath,
                                                        ex.LogEventMessage);
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
                            CancellationToken = cancellationToken,
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
    }
}
