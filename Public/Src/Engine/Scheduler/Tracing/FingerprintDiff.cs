// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Utilities class for diff-ing fingerprints irrespective of the stored data and of the diff data representation.
    /// </summary>
    internal static class FingerprintDiff
    {
        #region Extraction
        private static readonly List<string> s_emptyList = new List<string>();

        /// <summary>
        /// Extracts differences between two unordered maps (or dictionaries).
        /// </summary>
        internal static bool ExtractUnorderedMapDiff<T>(
                IReadOnlyDictionary<string, T> oldData,
                IReadOnlyDictionary<string, T> newData,
                Func<T, T, bool> equalValue,
                out IReadOnlyList<string> added,
                out IReadOnlyList<string> removed,
                out IReadOnlyList<string> changed)
        {
            bool hasDiff = ExtractUnorderedListDiff(oldData.Keys, newData.Keys, out added, out removed);
            List<string> mutableChanged = s_emptyList;

            foreach (var kvp in oldData)
            {
                if (newData.TryGetValue(kvp.Key, out var newValue) && !equalValue(kvp.Value, newValue))
                {
                    if (mutableChanged == s_emptyList)
                    {
                        mutableChanged = new List<string>();
                    }

                    mutableChanged.Add(kvp.Key);
                }
            }

            changed = mutableChanged;

            return hasDiff || changed.Count > 0;
        }

        /// <summary>
        /// Extracts differences between two unordered lists (or sets).
        /// </summary>
        internal static bool ExtractUnorderedListDiff(
            IEnumerable<string> oldData,
            IEnumerable<string> newData,
            out IReadOnlyList<string> added,
            out IReadOnlyList<string> removed)
        {
            bool oldAny = oldData.Any();
            bool newAny = newData.Any();

            if (!oldAny && !newAny)
            {
                added = removed = s_emptyList;
            }
            else if (oldAny && !newAny)
            {
                added = s_emptyList;
                removed = oldData.ToHashSet().ToList();
            }
            else if (!oldAny && newAny)
            {
                removed = s_emptyList;
                added = newData.ToHashSet().ToList();
            }
            else
            {
                var newSet = newData.ToHashSet();
                var oldSet = oldData.ToHashSet();
                newSet.ExceptWith(oldData);
                oldSet.ExceptWith(newData);
                added = newSet.ToList();
                removed = oldSet.ToList();
            }

            return added.Count > 0 || removed.Count > 0;
        }

        #endregion Extraction

        #region Internal fingerprint data

        /// <summary>
        /// Observed input data.
        /// </summary>
        internal struct ObservedInputData : IEquatable<ObservedInputData>
        {
            /// <summary>
            /// Path.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Flags.
            /// </summary>
            public readonly string Flags;

            /// <summary>
            /// Pattern.
            /// </summary>
            public readonly string Pattern;

            /// <summary>
            /// Access type.
            /// </summary>
            public readonly string AccessType;

            /// <summary>
            /// Content hash or membership hash.
            /// </summary>
            public readonly string Hash;

            /// <summary>
            /// Creates an instance of <see cref="ObservedInputData"/>.
            /// </summary>
            public ObservedInputData(
                string path,
                string flags,
                string pattern,
                string hashMarker,
                string hash)
            {
                Path = path;
                Flags = flags;
                Pattern = pattern;
                AccessType = hashMarker;
                Hash = hash;
            }

            /// <summary>
            /// Creates an instance of <see cref="ObservedInputData"/>.
            /// </summary>
            public ObservedInputData(string path, string flags, string pattern) : this(path, flags, pattern, null, null) { }

            /// <inheritdoc/>
            public bool Equals(ObservedInputData other) =>
                Path == other.Path && Flags == other.Flags && Pattern == other.Pattern && AccessType == other.AccessType && Hash == other.Hash;

            /// <inheritdoc/>
            public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(hashCode(Path), hashCode(Flags), hashCode(Pattern), hashCode(AccessType), hashCode(Hash));

                static int hashCode(string s) => s != null ? EqualityComparer<string>.Default.GetHashCode(s) : 0;
            }

            /// <summary>
            /// Describes diff with respect to other instance of <see cref="ObservedInputData"/>.
            /// </summary>
            /// <param name="data"></param>
            /// <returns></returns>
            public string DescribeDiffWithoutPath(ObservedInputData data) =>
                string.Join(
                    " | ",
                    (new[] {
                            Prefix(nameof(AccessType), ObservedInputConstants.ToExpandedString(AccessType)),
                            Flags == data.Flags ? null : Prefix(nameof(Flags), Flags),
                            Pattern == data.Pattern ? null : Prefix(nameof(Pattern), Pattern),
                            Hash == data.Hash ? null : Prefix(nameof(Hash), Hash) }).Where(s => !string.IsNullOrEmpty(s)));

            private string Prefix(string prefix, string item) => string.IsNullOrEmpty(item) ? null : prefix + ": " + item;
        }

        /// <summary>
        /// Input file data.
        /// </summary>
        internal struct InputFileData : IEquatable<InputFileData>
        {
            /// <summary>
            /// Path.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Content hash or content itself (in case of being written by a write-file pip).
            /// </summary>
            public readonly string HashOrContent;

            /// <summary>
            /// Creates an instance of <see cref="InputFileData"/>.
            /// </summary>
            public InputFileData(string path, string hashOrContent)
            {
                Path = path;
                HashOrContent = hashOrContent;
            }

            /// <inheritdoc/>
            public bool Equals(InputFileData other) => Path == other.Path && HashOrContent == other.HashOrContent;

            /// <inheritdoc/>
            public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(hashCode(Path), hashCode(HashOrContent));

                static int hashCode(string s) => s != null ? EqualityComparer<string>.Default.GetHashCode(s) : 0;
            }
        }

        /// <summary>
        /// Output file data.
        /// </summary>
        internal struct OutputFileData : IEquatable<OutputFileData>
        {
            /// <summary>
            /// Path.
            /// </summary>
            public readonly string Path;

            /// <summary>
            /// Attributes.
            /// </summary>
            public readonly string Attributes;

            /// <summary>
            /// Creates an instance of <see cref="OutputFileData"/>.
            /// </summary>
            public OutputFileData(string path, string attributes)
            {
                Path = path;
                Attributes = attributes;
            }

            /// <inheritdoc/>
            public bool Equals(OutputFileData other) => Path == other.Path && Attributes == other.Attributes;

            /// <inheritdoc/>
            public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(hashCode(Path), hashCode(Attributes));

                static int hashCode(string s) => s != null ? EqualityComparer<string>.Default.GetHashCode(s) : 0;
            }
        }

        /// <summary>
        /// Environment variable data.
        /// </summary>
        internal struct NameValuePairData : IEquatable<NameValuePairData>
        {
            /// <summary>
            /// Name.
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// Value.s
            /// </summary>
            public readonly string Value;

            /// <summary>
            /// Creates an instance of <see cref="NameValuePairData"/>.
            /// </summary>
            public NameValuePairData(string name, string value)
            {
                Name = name;
                Value = value;
            }

            /// <inheritdoc/>
            public bool Equals(NameValuePairData other) => Name == other.Name && Value == other.Value;

            /// <inheritdoc/>
            public override bool Equals(object obj) => StructUtilities.Equals(this, obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(hashCode(Name), hashCode(Value));

                static int hashCode(string s) => s != null ? EqualityComparer<string>.Default.GetHashCode(s) : 0;
            }
        }
       
        #endregion Internal fingerprint data

        #region Pools

        private static ObjectPool<Dictionary<string, T>> CreateMapPool<T>() => 
            new ObjectPool<Dictionary<string, T>>(
                () => new Dictionary<string, T>(),
                map => { map.Clear(); return map; });

        /// <summary>
        /// Pool for <see cref="InputFileData"/>.
        /// </summary>
        public static ObjectPool<Dictionary<string, InputFileData>> InputFileDataMapPool { get; } = CreateMapPool<InputFileData>();

        /// <summary>
        /// Pool for <see cref="OutputFileData"/>.
        /// </summary>
        public static ObjectPool<Dictionary<string, OutputFileData>> OutputFileDataMapPool { get; } = CreateMapPool<OutputFileData>();

        /// <summary>
        /// Pool for <see cref="NameValuePairData"/>.
        /// </summary>
        public static ObjectPool<Dictionary<string, NameValuePairData>> NameValuePairDataMapPool { get; } = CreateMapPool<NameValuePairData>();

        /// <summary>
        /// Pool for <see cref="ObservedInputData"/>.
        /// </summary>
        public static ObjectPool<Dictionary<string, ObservedInputData>> ObservedInputDataMapPool { get; } = CreateMapPool<ObservedInputData>();

        #endregion Pools
    }
}
