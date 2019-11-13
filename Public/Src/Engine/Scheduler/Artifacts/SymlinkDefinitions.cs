// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Symlink definitions containing mappings from symlinks to their targets.
    /// </summary>
    public sealed class SymlinkDefinitions
    {
        private readonly PathTable m_pathTable;
        private readonly Dictionary<AbsolutePath, List<AbsolutePath>> m_directorySymlinkContents;
        private readonly ConcurrentBigMap<AbsolutePath, AbsolutePath> m_symlinkDefinitionMap;

        private readonly HashSet<HierarchicalNameId> m_directoriesContainingSymlinks;

        /// <summary>
        /// Mappings from symlinks to their targets.
        /// </summary>
        public IReadOnlyDictionary<AbsolutePath, List<AbsolutePath>> DirectorySymlinkContents => m_directorySymlinkContents;

        /// <summary>
        /// Creates an instance of <see cref="SymlinkDefinitions" />.
        /// </summary>
        public SymlinkDefinitions(PathTable pathTable, ConcurrentBigMap<AbsolutePath, AbsolutePath> symlinkDefinitionMap)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(symlinkDefinitionMap != null);
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_symlinkDefinitionMap = symlinkDefinitionMap;
            m_directorySymlinkContents = new Dictionary<AbsolutePath, List<AbsolutePath>>();
            m_directoriesContainingSymlinks = new HashSet<HierarchicalNameId>();

            foreach (var symlink in m_symlinkDefinitionMap.Keys)
            {
                var directory = symlink.GetParent(m_pathTable);

                foreach (var pathId in m_pathTable.EnumerateHierarchyBottomUp(directory.Value))
                {
                    if (!m_directoriesContainingSymlinks.Add(pathId))
                    {
                        break;
                    }
                }

                List<AbsolutePath> paths;
                if (!m_directorySymlinkContents.TryGetValue(directory, out paths))
                {
                    paths = new List<AbsolutePath>();
                    m_directorySymlinkContents.Add(directory, paths);
                }

                paths.Add(symlink);
            }
        }

        /// <summary>
        /// Load symlink definitions serialized using <see cref="PathMapSerializer"/>
        /// </summary>
        public static async Task<Possible<SymlinkDefinitions>> TryLoadAsync(
            LoggingContext loggingContext,
            PathTable pathTable,
            string filePath,
            string symlinksDebugPath,
            ITempCleaner tempDirectoryCleaner = null)
        {
            try
            {
                var pathMap = await PathMapSerializer.LoadAsync(filePath, pathTable);
                var definitions = new SymlinkDefinitions(pathTable, pathMap);
                Logger.Log.SymlinkFileTraceMessage(
                    loggingContext,
                    I($"Loaded symlink definitions with {definitions.m_symlinkDefinitionMap.Count} entries and {definitions.m_directorySymlinkContents.Count} directories."));
                if (EngineEnvironmentSettings.DebugSymlinkDefinitions && symlinksDebugPath != null)
                {
                    FileUtilities.DeleteFile(symlinksDebugPath, tempDirectoryCleaner: tempDirectoryCleaner);
                    using (var writer = new StreamWriter(symlinksDebugPath))
                    {
                        foreach (var entry in pathMap)
                        {
                            writer.WriteLine("Source: {0}", entry.Key.ToString(pathTable));
                            writer.WriteLine("Target: {0}", entry.Value.ToString(pathTable));
                        }
                    }
                }

                return definitions;
            }
            catch (Exception ex)
            {
                Logger.Log.FailedLoadSymlinkFile(loggingContext, ex.GetLogEventMessage());
                return new Failure<string>("Failed loading symlink definition file");
            }
        }

        /// <summary>
        /// Deserialize symlink definitions serialized using <see cref="Serialize(BuildXLWriter, SymlinkDefinitions)"/>
        /// </summary>
        public static Possible<SymlinkDefinitions> Deserialize(LoggingContext loggingContext, PathTable pathTable, BuildXLReader reader)
        {
            try
            {
                bool isNull = reader.ReadBoolean();
                if (isNull)
                {
                    return (SymlinkDefinitions)null;
                }

                var pathMap = ConcurrentBigMap<AbsolutePath, AbsolutePath>.Deserialize(
                    reader,
                    () => new KeyValuePair<AbsolutePath, AbsolutePath>(
                        key: reader.ReadAbsolutePath(),
                        value: reader.ReadAbsolutePath()));

                return new SymlinkDefinitions(pathTable, pathMap);
            }
            catch (Exception ex)
            {
                Logger.Log.FailedLoadSymlinkFile(loggingContext, ex.GetLogEventMessage());
                return new Failure<string>("Failed loading symlink definition file");
            }
        }

        /// <summary>
        /// Serialize the symlink definitions for load using <see cref="Deserialize"/>
        /// </summary>
        /// <param name="writer">the writer</param>
        /// <param name="symlinkDefinitions">the symlink definitions (may be null)</param>
        public static void Serialize(BuildXLWriter writer, SymlinkDefinitions symlinkDefinitions)
        {
            writer.Write(symlinkDefinitions == null);
            symlinkDefinitions?.m_symlinkDefinitionMap.Serialize(
                writer,
                kvp =>
                {
                    writer.Write(kvp.Key);
                    writer.Write(kvp.Value);
                });
        }

        /// <summary>
        /// Checks if symlink definition contains the given path as symlink.
        /// </summary>
        public bool IsSymlink(AbsolutePath path) => m_symlinkDefinitionMap.ContainsKey(path);

        /// <summary>
        /// Gets the symlink target or <see cref="AbsolutePath.Invalid"/> if the path is not a registered symlink
        /// </summary>
        public AbsolutePath TryGetSymlinkTarget(AbsolutePath symlink) => m_symlinkDefinitionMap.TryGet(symlink).Item.Value;

        /// <summary>
        /// Indexer.
        /// </summary>
        public AbsolutePath this[AbsolutePath path] => m_symlinkDefinitionMap[path];

        /// <summary>
        /// Tries to get symlink target.
        /// </summary>
        public bool TryGetSymlinkTarget(AbsolutePath symlink, out AbsolutePath symlinkTarget)
            => m_symlinkDefinitionMap.TryGetValue(symlink, out symlinkTarget);

        /// <summary>
        /// Checks if a directory contains symlinks.
        /// </summary>
        public bool DirectoryContainsSymlink(AbsolutePath directory) => m_directorySymlinkContents.ContainsKey(directory);

        /// <summary>
        /// Tries to get all symlinks inside a given directory.
        /// </summary>
        public bool TryGetSymlinksInDirectory(AbsolutePath directory, out IReadOnlyList<AbsolutePath> paths)
        {
            paths = null;
            List<AbsolutePath> tempPaths;

            if (!m_directorySymlinkContents.TryGetValue(directory, out tempPaths))
            {
                return false;
            }

            paths = tempPaths;
            return true;
        }

        /// <summary>
        /// Checks whether the directory or any of its children contain symlinks
        /// </summary>
        public bool HasNestedSymlinks(AbsolutePath directory)
        {
            return m_directoriesContainingSymlinks.Contains(directory.Value);
        }

        /// <summary>
        /// Number of mappings in this instance of <see cref="SymlinkDefinitions"/>.
        /// </summary>
        public int Count => m_symlinkDefinitionMap.Count;
    }
}
