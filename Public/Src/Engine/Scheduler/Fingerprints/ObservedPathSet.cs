// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

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
            [AllowNull] UnsafeOptions unsafeOptions)
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
        public async Task<ContentHash> ToContentHash(PathTable pathTable, PathExpander pathExpander, bool preservePathCasing)
        {
            using (var pathSetBuffer = new System.IO.MemoryStream())
            using (var writer = new BuildXLWriter(stream: pathSetBuffer, debug: false, leaveOpen: true, logStats: false))
            {
                Serialize(pathTable, writer, preservePathCasing, pathExpander);
                return await ContentHashingUtilities.HashContentStreamAsync(pathSetBuffer);
            }
        }


        #region Serialization

        /// <nodoc />
        public void Serialize(
            PathTable pathTable,
            BuildXLWriter writer,
            bool preserveCasing,
            PathExpander pathExpander = null,
            Action<BuildXLWriter, AbsolutePath> pathWriter = null,
            Action<BuildXLWriter, StringId> stringWriter = null)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                // CODESYNC: JsonFingerprinter.AddFileName mirrors this logic
                preserveCasing = true;
            }

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
                    string expanded = pathExpander?.ExpandPath(pathTable, entry.Path) ?? entry.Path.ToString(pathTable);
                    if (!preserveCasing)
                    {
                        expanded = expanded.ToCanonicalizedPath();
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
                if (!compareFileNames(ObservedAccessedFileNames[i - 1], ObservedAccessedFileNames[i], preserveCasing, ObservedAccessedFileNames.Comparer))
                {
                    fileNameCountWithoutDuplicates++;
                }
            }

            writer.WriteCompact(fileNameCountWithoutDuplicates);
            StringId lastFileName = StringId.Invalid;
            foreach (var entry in ObservedAccessedFileNames)
            {
                if (compareFileNames(lastFileName, entry, preserveCasing, ObservedAccessedFileNames.Comparer))
                {
                    continue;
                }

                if (stringWriter != null)
                {
                    stringWriter(writer, entry);
                }
                else
                {
                    var expandedFileName = entry.ToString(pathTable.StringTable);
                    if (!preserveCasing)
                    {
                        expandedFileName = expandedFileName.ToCanonicalizedPath();
                    }

                    writer.Write(expandedFileName);
                }

                lastFileName = entry;
            }

            // Serializing UnsafeOptions
            UnsafeOptions.Serialize(writer);

            bool compareFileNames(StringId a, StringId b, bool preserveCasing, CaseInsensitiveStringIdComparer caseInsensitiveComparer)
            {
                return !preserveCasing ? caseInsensitiveComparer.Compare(a, b) == 0 : a == b;
            }
        }

        /// <summary>
        /// Fast incremental constructor for an <see cref="AbsolutePath"/> when the caller has a
        /// previously-built <paramref name="reference"/> expanded path, and a new path whose first
        /// <paramref name="sharedPrefixLength"/> characters are - by construction - byte-identical
        /// to the first <paramref name="sharedPrefixLength"/> characters of
        /// <see cref="ExpandedAbsolutePath.ExpandedPath"/>.
        ///
        /// Avoids re-parsing the full string and re-inserting every shared component into the
        /// <see cref="PathTable"/> (which would each take a writer lock on its striped hash set).
        /// On any failure (no usable common ancestor, ascend past root, invalid PathAtom) returns
        /// false and the caller must fall back to <see cref="AbsolutePath.TryCreate(PathTable, string, out AbsolutePath)"/>.
        ///
        /// Private to <see cref="ObservedPathSet"/> because the contract that <paramref name="reference"/>'s
        /// expanded path faithfully matches its <see cref="AbsolutePath"/> and that
        /// <paramref name="sharedPrefixLength"/> is a correct shared-prefix length holds only by the
        /// construction of <see cref="TryDeserialize"/>.
        /// </summary>
        private static bool TryFastConstructAbsolutePath(
            PathTable pathTable,
            ExpandedAbsolutePath reference,
            string newExpandedPath,
            int sharedPrefixLength,
            out AbsolutePath result)
        {
            result = AbsolutePath.Invalid;
            string referenceExpandedPath = reference.ExpandedPath;
            char separator = Path.DirectorySeparatorChar;

            // Scan backward within the shared prefix to find the last separator. Everything before that
            // separator forms a clean common-ancestor boundary between the two paths.
            // Backward scan terminates after roughly one leaf-component-length of characters, which is
            // typically far less than sharedPrefixLength.
            int lastSeparatorIndex = -1;
            for (int k = sharedPrefixLength - 1; k >= 0; k--)
            {
                if (referenceExpandedPath[k] == separator)
                {
                    lastSeparatorIndex = k;
                    break;
                }
            }

            if (lastSeparatorIndex < 0)
            {
                // Shared prefix contains no separator - no usable common ancestor.
                return false;
            }

            // Count components in referencePath strictly past the ancestor at lastSeparatorIndex. That's
            // the number of separators in referenceExpandedPath strictly after lastSeparatorIndex, plus 1
            // for the leaf component (assuming referenceExpandedPath does not end in a separator).
            // The backward scan above proved [lastSeparatorIndex + 1 .. sharedPrefixLength - 1] has no
            // separators, so we can skip that range and start the forward walk at sharedPrefixLength.
            int ascendBy = 1;
            for (int k = sharedPrefixLength; k < referenceExpandedPath.Length; k++)
            {
                if (referenceExpandedPath[k] == separator)
                {
                    ascendBy++;
                }
            }

            AbsolutePath ancestor = reference.Path;
            for (int k = 0; k < ascendBy; k++)
            {
                ancestor = ancestor.GetParent(pathTable);
                if (!ancestor.IsValid)
                {
                    // Walked past the root.
                    return false;
                }
            }

            // Build a RelativePath from newExpandedPath[lastSeparatorIndex + 1 .. end] and combine it
            // with the ancestor.
            int suffixStart = lastSeparatorIndex + 1;
            int suffixLength = newExpandedPath.Length - suffixStart;
            if (suffixLength == 0)
            {
                // newExpandedPath ends in a separator - no leaf component to combine. Bail to the slow
                // path which will surface the malformed-path error consistently with the non-fast-path
                // behavior.
                return false;
            }

            var suffixSegment = new StringSegment(newExpandedPath, suffixStart, suffixLength);
            if (!RelativePath.TryCreate(pathTable.StringTable, suffixSegment, out RelativePath suffixRelativePath))
            {
                return false;
            }

            result = ancestor.Combine(pathTable, suffixRelativePath);
            return true;
        }

        /// <nodoc />
        public static Possible<ObservedPathSet, DeserializeFailure> TryDeserialize(
    PathTable pathTable,
    BuildXLReader reader,
    PathExpander pathExpander = null,
    Func<BuildXLReader, AbsolutePath> pathReader = null,
    Func<BuildXLReader, StringId> stringReader = null)
        {
            try
            {
                PathTable.ExpandedAbsolutePathComparer comparer = pathTable.ExpandedPathComparer;

                int pathCount = reader.ReadInt32Compact();
                ObservedPathEntry[] paths = new ObservedPathEntry[pathCount];

                string lastStr = null;
                // Tracks the AbsolutePath built for the previous entry together with its expanded
                // string form, so we can build the next path incrementally via PathTable.GetParent/Combine
                // instead of re-parsing the full string. Invalid means the previous entry was produced
                // via PathExpander.TryCreatePath (tokenized form) or is otherwise unsafe for string-prefix-
                // based ancestor inference.
                ExpandedAbsolutePath lastReference = ExpandedAbsolutePath.Invalid;
                for (int i = 0; i < pathCount; i++)
                {
                    var flags = (ObservedPathEntryFlags)reader.ReadByte();
                    string enumeratePatternRegex = null;
                    if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern) != 0)
                    {
                        enumeratePatternRegex = reader.ReadString();
                    }
                    else if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithAllPattern) != 0)
                    {
                        enumeratePatternRegex = RegexDirectoryMembershipFilter.AllowAllRegex;
                    }

                    AbsolutePath newPath = AbsolutePath.Invalid;
                    string full = null;

                    if (pathReader != null)
                    {
                        newPath = pathReader(reader);
                    }
                    else
                    {
                        int reuseCount = reader.ReadInt32Compact();
                        bool fastConstructionApplied = false;

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

                            full = lastStr.Substring(0, reuseCount) + reader.ReadString();

                            // Fast path: try to build the new AbsolutePath incrementally from lastReference by
                            // ascending to the closest common ancestor (an existing AbsolutePath whose expanded
                            // form is a prefix of both lastStr and the new path) and then combining the trailing
                            // path components. This avoids re-tokenizing the full path string and re-inserting
                            // every shared component into the PathTable.
                            //
                            // Correct by construction: lastReference.ExpandedPath is the expansion of
                            // lastReference.Path (when the previous entry came from this same loop and was not
                            // produced via PathExpander), and full[0..reuseCount] == lastStr[0..reuseCount] (it
                            // was just copied via Substring). So the divergence point is exactly reuseCount.
                            if (lastReference.IsValid
                                && TryFastConstructAbsolutePath(
                                    pathTable,
                                    reference: lastReference,
                                    newExpandedPath: full,
                                    sharedPrefixLength: reuseCount,
                                    out newPath))
                            {
                                fastConstructionApplied = true;
                            }
                        }

                        if (fastConstructionApplied)
                        {
                            // Fast path produced an AbsolutePath whose expanded form is 'full' by construction.
                            lastReference = ExpandedAbsolutePath.CreateUnsafe(newPath, full);
                        }
                        else if (!AbsolutePath.TryCreate(pathTable, full, out newPath))
                        {
                            // It might be failed due to the tokenized path.
                            if (pathExpander == null || !pathExpander.TryCreatePath(pathTable, full, out newPath))
                            {
                                return new DeserializeFailure($"Invalid path: '{full}'");
                            }

                            // newPath came from the expander - its expansion may not match the literal
                            // 'full' string, so the next entry cannot use string-based ancestor inference.
                            lastReference = ExpandedAbsolutePath.Invalid;
                        }
                        else
                        {
                            lastReference = ExpandedAbsolutePath.CreateUnsafe(newPath, full);
                        }
                    }

                    paths[i] = new ObservedPathEntry(newPath, flags, enumeratePatternRegex);

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

                // This preserves the sort order that was serialized, which may not actually be a locally accurate sort order since sorting
                // can vary based on machine and this may have been serialized on another machine. The sort order is only here for consistency
                // rather than actually needing a particular order. So just maintaining it from serialization should be fine.
                // At one point in the past these was an assert that the sort order was maintained in debug builds. But that was removed
                // because it crashes debug builds of the analyzer when reloading graphs across operating systems.
                return new ObservedPathSet(
                    SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<ObservedPathEntry>.FromWithoutCopy(paths),
                        new ObservedPathEntryExpandedPathComparer(comparer)),
                    SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                        ReadOnlyArray<StringId>.FromWithoutCopy(fileNames),
                        new CaseInsensitiveStringIdComparer(pathTable.StringTable)),
                    unsafeOptions);
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException)
            {
                return new DeserializeFailure($"Failed to read from stream {ex}");
            }
        }

        #endregion Serialization
    }
}
