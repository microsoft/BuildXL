// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Computes a <see cref="DirectoryFingerprint" /> for any directory path,
    /// based on some combination of real filesystem state and outputs as declared in a static pip graph.
    /// </summary>
    /// <remarks>
    /// We compute fingerprints of directory membership so that we can know to re-run processes following
    /// changes to directories which they enumerated.
    ///
    /// Based on the readability / writability of a path's mount, different fingerprinting strategies are used:
    /// - Unhashable mounts get a null fingerprint. Changes to them should not affect incremental builds.
    /// - Read-only mounts are fingerprinted based on actual filesystem state. Read-only mounts tend to be sources (user-writable).
    /// - Writable mounts are fingeprinted based on the static build graph. Examining the filesystem state under these mounts is not reliable since
    /// it is expected to change concurrently with directory fingerprinting.
    ///
    /// Casing: Filenames are case-canonicalized before fingerprinting. Changing the casing of a file does not change its directory's fingerprint.
    ///
    /// This class provides a cache of directory fingerprints (each directory has a fingerprint calculated at most once),
    /// and is thread safe. The expected usage is to have one directory fingerprinter per build session.
    /// </remarks>
    public sealed class DirectoryMembershipFingerprinter : IDirectoryMembershipFingerprinter
    {
        private readonly LoggingContext m_loggingContext;
        private readonly PipExecutionContext m_context;

        /// <summary>
        /// Cache of fingerprints computed from the filesystem or full graph
        /// </summary>
        private readonly ConcurrentDictionary<(AbsolutePath, string), Lazy<DirectoryFingerprint?>> m_fingerprints;

        /// <summary>
        /// Cache of directory contents that are enumerated via full graph or filesystem.
        /// </summary>
        public readonly ObjectCache<AbsolutePath, Lazy<DirectoryEnumerationResult>> CachedDirectoryContents;

        private readonly IExecutionLogTarget m_executionLog;

        /// <summary>
        /// Used to make sure only one instance of a graph based fingerprinting special case is logged per directory
        /// </summary>
        private readonly HashSet<AbsolutePath> m_pipGraphRuleSpecialCasesLogged = new HashSet<AbsolutePath>();

        /// <nodoc />
        public DirectoryMembershipFingerprinter(
            LoggingContext loggingContext,
            PipExecutionContext context,
            IExecutionLogTarget executionLog = null)
        {
            Contract.Requires(context != null);

            m_loggingContext = loggingContext;
            m_context = context;
            m_executionLog = executionLog;
            m_fingerprints = new ConcurrentDictionary<(AbsolutePath, string), Lazy<DirectoryFingerprint?>>();

            // 100k capacity was determined according to the Office x64 codebase, which has around 120k unique directory enumerations.
            CachedDirectoryContents = new ObjectCache<AbsolutePath, Lazy<DirectoryEnumerationResult>>(100000);
        }

        public DirectoryFingerprint? TryComputeDirectoryFingerprint(
            AbsolutePath directoryPath,
            CacheablePipInfo cachePipInfo,
            Func<EnumerationRequest, PathExistence?> tryEnumerateDirectory,
            bool cacheableFingerprint,
            DirectoryMembershipFingerprinterRule rule,
            DirectoryMembershipHashedEventData eventData)
        {
            Contract.Requires(directoryPath.IsValid);

            if (cacheableFingerprint)
            {
                Contract.Assert(!eventData.IsSearchPath);

                string enumerateFilter = eventData.EnumeratePatternRegex ?? RegexDirectoryMembershipFilter.AllowAllRegex;
                // Filesystem fingerprints and fingerprints from the full graph may be cached if the filter is AllowAll.
                return m_fingerprints.GetOrAdd((directoryPath, enumerateFilter), Lazy.Create(
                    () => TryComputeDirectoryFingerprintInternal(
                        directoryPath, 
                        cachePipInfo, 
                        tryEnumerateDirectory, 
                        rule, 
                        eventData))).Value;
            }

            eventData.PipId = cachePipInfo.PipId;
            return TryComputeDirectoryFingerprintInternal(
                directoryPath,
                cachePipInfo,
                tryEnumerateDirectory,
                rule,
                eventData);
        }

        private DirectoryFingerprint? TryComputeDirectoryFingerprintInternal(
            AbsolutePath directoryPath,
            CacheablePipInfo process,
            Func<EnumerationRequest, PathExistence?> tryEnumerateDirectory,
            DirectoryMembershipFingerprinterRule rule,
            DirectoryMembershipHashedEventData eventData)
        {            
            var expandedDirectoryPath = directoryPath.ToString(m_context.PathTable);

            // Log a message if a rule to disable filesystem enumeration is being used
            if (rule != null && rule.DisableFilesystemEnumeration)
            {
                lock (m_pipGraphRuleSpecialCasesLogged)
                {
                    if (m_pipGraphRuleSpecialCasesLogged.Add(directoryPath))
                    {
                        Logger.Log.DirectoryFingerprintExercisedRule(m_loggingContext, rule.Name, expandedDirectoryPath);
                    }
                }
            }

            DirectoryFingerprint result;
            PathExistence? existence;
            int numMembers = 0;

            using (var pooledList = Pools.GetStringList())
            {
                var directoryMembers = new List<AbsolutePath>();
                var fileNames = pooledList.Instance;

                // Actually perform the enumeration and compute the fingerprint
                Action<AbsolutePath, string> handleEntry = (path, fileName) =>
                {
                    if (rule != null && !rule.DisableFilesystemEnumeration)
                    {
                        if (rule.ShouldIgnoreFileWhenEnumerating(fileName))
                        {
                            Logger.Log.DirectoryFingerprintExercisedRule(m_loggingContext, rule.Name, path.ToString(m_context.PathTable));
                            return;
                        }
                    }

                    fileNames.Add(fileName);
                    directoryMembers.Add(path);
                };

                existence = tryEnumerateDirectory(new EnumerationRequest(CachedDirectoryContents, directoryPath, process, handleEntry));
                    
                if (existence == null)
                {
                    return null;
                }

                numMembers = fileNames.Count;
                // we sort members here, so they are stored in the same order they are added to a fingerprint in CalculateDirectoryFingerprint
                directoryMembers.Sort(m_context.PathTable.ExpandedPathComparer);
                eventData.Members = directoryMembers;
                result = CalculateDirectoryFingerprint(fileNames);
            }

            switch (existence)
            {
                case PathExistence.ExistsAsDirectory:
                case PathExistence.ExistsAsFile:
                    // The file can be a directory symlink or junction, in which case it is classified as a file.
                    break;
                case PathExistence.Nonexistent:
                    // We return an all-zero fingerprint for a non-existent path.
                    // This is equivalent to the static-graph case (see ComputeDirectoryFingerprintFromPipGraph)
                    // since enumerating nonexistent-on-disk path in the path table is valid (though doing so returns an empty result).
                    Contract.Assume(result.Hash == ContentHashingUtilities.ZeroHash);
                    break;
                default:
                    throw Contract.AssertFailure("Unhandled PathExistence");
            }

            eventData.DirectoryFingerprint = result;
            m_executionLog?.DirectoryMembershipHashed(eventData);

            if (eventData.IsStatic)
            {
                Logger.Log.DirectoryFingerprintComputedFromGraph(
                    m_loggingContext,
                    expandedDirectoryPath,
                    result.ToString(),
                    numMembers,
                    process.Description);
            }
            else
            {
                Logger.Log.DirectoryFingerprintComputedFromFilesystem(
                    m_loggingContext,
                    expandedDirectoryPath,
                    result.ToString(),
                    numMembers);
            }

            return result;
        }

        /// <summary>
        /// Returns a hash for the list of the file names, after case-normalizing them.
        /// </summary>
        private static DirectoryFingerprint CalculateDirectoryFingerprint(IReadOnlyList<string> fileNames)
        {
            if (fileNames.Count == 0)
            {
                return DirectoryFingerprint.Zero;
            }

            string orderedFileNames = string.Join(",", fileNames.OrderBy(a => a, StringComparer.OrdinalIgnoreCase));
            byte[] nameBytes = Encoding.Unicode.GetBytes(orderedFileNames.ToUpperInvariant());
            var hash = ContentHashingUtilities.CreateFrom(MurmurHash3.Create(nameBytes, 0));
            return new DirectoryFingerprint(hash);
        }
    }
}
