// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Set of paths projected from <see cref="ObservedInput"/>s. In two-phase fingerprinting, the set of paths from a prior run of a tool
    /// (ignoring content) is used to derive a <see cref="BuildXL.Engine.Cache.Fingerprints.StrongContentFingerprint"/>. The path-sets are expected to be heavily duplicated
    /// across executions, i.e., a few path-sets generate many distinct strong fingerprints (due to differences in e.g. C++ header content).
    ///
    /// Since access-order of files is heavily non-deterministic, path-sets are canonicalized by sorting. This also facilitates excellent
    /// and cheap prefix-compression (many adjacent paths tend to share a long prefix).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ObservedPathSet
    {
        /// <summary>
        /// Constants used for labeling.
        /// </summary>
        public readonly struct Labels
        {
            /// <summary>
            /// Label for <see cref="ObservedPathSet.UnsafeOptions"/>.
            /// </summary>
            public const string UnsafeOptions = nameof(ObservedPathSet.UnsafeOptions);

            /// <summary>
            /// Label for <see cref="ObservedPathSet.ObservedAccessedFileNames"/>.
            /// </summary>
            public const string ObservedAccessedFileNames = nameof(ObservedPathSet.ObservedAccessedFileNames);

            /// <summary>
            /// Label for <see cref="Paths"/>.
            /// </summary>
            public const string Paths = nameof(ObservedPathSet.Paths);
        }

        /// <summary>
        /// Failure describing why deserialization of a path set failed (<see cref="ObservedPathSet.TryDeserialize"/>).
        /// </summary>
        public class DeserializeFailure : Failure
        {
            private readonly Failure<string> m_failure;

            /// <summary>
            /// <see cref="Failure{String}"/>
            /// </summary>
            public DeserializeFailure(string description, Failure innerFailure = null)
            {
                m_failure = new Failure<string>(description, innerFailure);
            }

            /// <inheritdoc />
            public override BuildXLException CreateException() => m_failure.CreateException();

            /// <inheritdoc />
            public override string Describe() => m_failure.Describe();

            /// <inheritdoc />
            public override BuildXLException Throw() => m_failure.Throw();
        }

        /// <summary>
        /// Represented paths. Note that the array may contain duplicates, but they are semantically redundant (and may
        /// be collapsed together under a serialization roundtrip).
        /// </summary>
        public readonly SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer> Paths;

        /// <summary>
        /// Observed accesses file names
        /// </summary>
        public readonly SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> ObservedAccessedFileNames;

        /// <summary>
        /// Unsafe options used to run the pip.
        /// </summary>
        [NotNull]
        public readonly UnsafeOptions UnsafeOptions;

        /// <summary>
        /// Constructs a path set from already-sorted paths. The array may contain duplicates.
        /// </summary>
        public ObservedPathSet(
            SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer> paths,
            SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer> observedAccessedFileNames,
            [CanBeNull]UnsafeOptions unsafeOptions)
        {
            Contract.Requires(paths.IsValid);
            Paths = paths;
            ObservedAccessedFileNames = observedAccessedFileNames;
            UnsafeOptions = unsafeOptions ?? UnsafeOptions.SafeValues;
        }

        /// <summary>
        /// Returns a new instance of <see cref="ObservedPathSet"/> with updated <see cref="UnsafeOptions"/> value.
        /// </summary>
        public ObservedPathSet WithUnsafeOptions(UnsafeOptions newUnsafeOptions)
        {
            return new ObservedPathSet(Paths, ObservedAccessedFileNames, newUnsafeOptions);
        }

        /// <summary>
        /// Computes content hash of this object (by serializing it and hashing the serialized bytes).
        /// </summary>
        public async Task<ContentHash> ToContentHash(PathTable pathTable, PathExpander pathExpander)
        {
            using (var pathSetBuffer = new System.IO.MemoryStream())
            using (var writer = new BuildXLWriter(stream: pathSetBuffer, debug: false, leaveOpen: true, logStats: false))
            {
                Serialize(pathTable, writer, pathExpander);
                return await ContentHashingUtilities.HashContentStreamAsync(pathSetBuffer);
            }
        }


        #region Serialization

        /// <nodoc />
        public void Serialize(
            PathTable pathTable,
            BuildXLWriter writer,
            PathExpander pathExpander = null,
            Action<BuildXLWriter, AbsolutePath> pathWriter = null,
            Action<BuildXLWriter, StringId> stringWriter = null)
        {
            // We allow duplicates on construction (and deserialization), but attempt to remove them here.
            // This isn't required for correctness, but may result in storing fewer PathSets.
            int countWithoutDuplicates = Paths.Length == 0 ? 0 : 1;
            for (int i = 1; i < Paths.Length; i++)
            {
                if (Paths[i - 1].Path != Paths[i].Path)
                {
                    countWithoutDuplicates++;
                }
            }

            writer.WriteCompact(countWithoutDuplicates);

            AbsolutePath last = AbsolutePath.Invalid;
            string lastExpanded = null;
            foreach (ObservedPathEntry entry in Paths)
            {
                if (last == entry.Path)
                {
                    continue;
                }

                writer.Write((byte)entry.Flags);
                if (entry.DirectoryEnumerationWithCustomPattern)
                {
                    writer.Write(entry.EnumeratePatternRegex);
                }

                if (pathWriter != null)
                {
                    pathWriter(writer, entry.Path);
                }
                else
                {
                    // Try to tokenize the path if the pathExpander is given.
                    string expanded =  pathExpander?.ExpandPath(pathTable, entry.Path) ?? entry.Path.ToString(pathTable);
                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        expanded = expanded.ToUpperInvariant();
                    }

                    int reuseCount = 0;
                    if (lastExpanded != null)
                    {
                        int limit = Math.Min(lastExpanded.Length, expanded.Length);
                        for (; reuseCount < limit && expanded[reuseCount] == lastExpanded[reuseCount]; reuseCount++);
                    }

                    if (OperatingSystemHelper.IsUnixOS && reuseCount == 1)
                    {
                        // As the root is denoted via '/' but the same symbol is also used as path separator,
                        // we cannot reuse the root path only when paths differ from the 2nd char onwards.
                        reuseCount = 0;
                    }

                    writer.WriteCompact(reuseCount);
                    writer.Write(reuseCount == 0 ? expanded : expanded.Substring(reuseCount));
                    lastExpanded = expanded;
                }

                last = entry.Path;
            }

            // Serializing observedAccessedFileNames
            int fileNameCountWithoutDuplicates = ObservedAccessedFileNames.Length == 0 ? 0 : 1;
            for (int i = 1; i < ObservedAccessedFileNames.Length; i++)
            {
                if (ObservedAccessedFileNames[i - 1] != ObservedAccessedFileNames[i])
                {
                    fileNameCountWithoutDuplicates++;
                }
            }

            writer.WriteCompact(fileNameCountWithoutDuplicates);
            StringId lastFileName = StringId.Invalid;
            foreach (var entry in ObservedAccessedFileNames)
            {
                if (lastFileName == entry)
                {
                    continue;
                }

                if (stringWriter != null)
                {
                    stringWriter(writer, entry);
                }
                else
                {
                    writer.Write(entry.ToString(pathTable.StringTable));
                }
            }

            // Serializing UnsafeOptions
            UnsafeOptions.Serialize(writer);
        }

        /// <nodoc />
        public static Possible<ObservedPathSet, DeserializeFailure> TryDeserialize(
            PathTable pathTable,
            BuildXLReader reader,
            PathExpander pathExpander = null,
            Func<BuildXLReader, AbsolutePath> pathReader = null,
            Func<BuildXLReader, StringId> stringReader = null)
        {
            PathTable.ExpandedAbsolutePathComparer comparer = pathTable.ExpandedPathComparer;

            int pathCount = reader.ReadInt32Compact();
            ObservedPathEntry[] paths = new ObservedPathEntry[pathCount];

            string lastStr = null;
            AbsolutePath lastPath = default(AbsolutePath);
            for (int i = 0; i < pathCount; i++)
            {
                var flags = (ObservedPathEntryFlags) reader.ReadByte();
                string enumeratePatternRegex = null;
                if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern) != 0)
                {
                    enumeratePatternRegex = reader.ReadString();
                }
                else if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithAllPattern) != 0)
                {
                    enumeratePatternRegex = RegexDirectoryMembershipFilter.AllowAllRegex;
                }

                AbsolutePath newPath;
                string full = null;

                if (pathReader != null)
                {
                    newPath = pathReader(reader);
                }
                else
                {
                    int reuseCount = reader.ReadInt32Compact();

                    if (reuseCount == 0)
                    {
                        full = reader.ReadString();
                    }
                    else
                    {
                        if (lastStr == null || lastStr.Length < reuseCount)
                        {
                            // This path set is invalid.
                            return new DeserializeFailure($"Invalid reuseCount: {reuseCount}; last: '{lastStr}', last string length: {lastStr?.Length}");
                        }

                        string partial = reader.ReadString();
                        full = lastStr.Substring(0, reuseCount) + partial;
                    }

                    if (!AbsolutePath.TryCreate(pathTable, full, out newPath))
                    {
                        // It might be failed due to the tokenized path.
                        if (pathExpander == null || !pathExpander.TryCreatePath(pathTable, full, out newPath))
                        {
                            return new DeserializeFailure($"Invalid path: '{full}'");
                        }
                    }
                }

                paths[i] = new ObservedPathEntry(newPath, flags, enumeratePatternRegex);

                if (lastPath.IsValid)
                {
#if DEBUG
                    if (comparer.Compare(lastPath, newPath) >= 0)
                    {
                        return new DeserializeFailure($"Paths not sorted: " +
                            $"old = '{lastPath.ToString(pathTable)}', new = '{newPath.ToString(pathTable)}';" +
                            $"old str = '{lastStr}', new str = '{full}'");
                    }
#endif
                }

                lastPath = newPath;
                lastStr = full;
            }

            int fileNameCount = reader.ReadInt32Compact();
            StringId[] fileNames = new StringId[fileNameCount];
            for (int i = 0; i < fileNameCount; i++)
            {
                fileNames[i] = stringReader?.Invoke(reader) ?? StringId.Create(pathTable.StringTable, reader.ReadString());
            }

            // Read unsafe options
            var unsafeOptions = UnsafeOptions.TryDeserialize(reader);
            if (unsafeOptions == null)
            {
                return new DeserializeFailure("UnsafeOptions are null");
            }

            // Note that we validated sort order above.
            return new ObservedPathSet(
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<ObservedPathEntry>.FromWithoutCopy(paths),
                    new ObservedPathEntryExpandedPathComparer(comparer)),
                SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<StringId>.FromWithoutCopy(fileNames),
                    new CaseInsensitiveStringIdComparer(pathTable.StringTable)),
                unsafeOptions);
        }

        #endregion Serialization
    }
}
