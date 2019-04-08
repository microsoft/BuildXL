// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Engine
{
    /// <summary>
    /// Observed graph inputs.
    /// </summary>
    /// <remarks>
    /// An observed graph input consists of <see cref="GraphPathInput"/> and <see cref="EnvironmentVariableInput"/>.
    /// The former specifies the paths, either content reads or directory enumerations, that are inputs to the graph construction.
    /// The latter specifies the environment variables that are used during the graph construction.
    /// This class is used to make the path inputs and envrionment inputs sorted, by path and by name, respectively.
    /// Having sorted inputs make it easy to manipulate the object, e.g., performing set minus. Instances of
    /// this class are then serialized into <see cref="PipGraphInputDescriptor"/> where <see cref="AbsolutePath"/> in the path inputs
    /// are replaced by their string counterparts.
    /// </remarks>
    public readonly struct ObservedGraphInputs
    {
        /// <summary>
        /// Path inputs.
        /// TODO: For perf, we may want to change it with sorted map.
        /// </summary>
        public readonly SortedReadOnlyArray<GraphPathInput, GraphPathInput.ByPathAndKindComparer> PathInputs;

        /// <summary>
        /// Environment variable inputs.
        /// TODO: For perf, we may want to change it with sorted map.
        /// </summary>
        public readonly SortedReadOnlyArray<EnvironmentVariableInput, EnvironmentVariableInput.ByNameComparer> EnvironmentVariableInputs;

        /// <summary>
        /// Mount inputs.
        /// TODO: For perf, we may want to change it with sorted map.
        /// </summary>
        public readonly SortedReadOnlyArray<MountInput, MountInput.ByNameComparer> MountInputs;

        /// <summary>
        /// True if path and environment variables inputs are empty.
        /// </summary>
        public bool IsEmpty => PathInputs.Length == 0 && EnvironmentVariableInputs.Length == 0 && MountInputs.Length == 0;

        /// <summary>
        /// Creates an instance of <see cref="ObservedGraphInputs"/>.
        /// </summary>
        public ObservedGraphInputs(
            SortedReadOnlyArray<GraphPathInput, GraphPathInput.ByPathAndKindComparer> pathInputs,
            SortedReadOnlyArray<EnvironmentVariableInput, EnvironmentVariableInput.ByNameComparer> environmentVariableInputs,
            SortedReadOnlyArray<MountInput, MountInput.ByNameComparer> mountInputs)
        {
            Contract.Requires(pathInputs.IsValid);
            Contract.Requires(environmentVariableInputs.IsValid);
            Contract.Requires(mountInputs.IsValid);

            PathInputs = pathInputs;
            EnvironmentVariableInputs = environmentVariableInputs;
            MountInputs = mountInputs;
        }

        /// <summary>
        /// Creates a new instace of <see cref="ObservedGraphInputs"/> by substracting it from a given <see cref="ObservedGraphInputs"/>.
        /// </summary>
        public ObservedGraphInputs Except(in ObservedGraphInputs other)
        {
            return new ObservedGraphInputs(
                PathInputs.ExceptWith(other.PathInputs),
                EnvironmentVariableInputs.ExceptWith(other.EnvironmentVariableInputs),
                MountInputs.ExceptWith(other.MountInputs));
        }

        /// <summary>
        /// Creates an instance of <see cref="PipGraphInputDescriptor"/> from this instance.
        /// </summary>
        public PipGraphInputDescriptor ToPipGraphInputDescriptor(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            return new PipGraphInputDescriptor
            {
                ObservedInputsSortedByPath = PathInputs.Select(i => i.ToStringKeyedHashObservedInput(pathTable)).ToList(),
                EnvironmentVariablesSortedByName =
                        EnvironmentVariableInputs.BaseArray.Select(kvp => new StringKeyValue { Key = kvp.Name, Value = kvp.Value }).ToList(),
                MountsSortedByName = MountInputs.Select(i => i.ToStringKeyValue(pathTable)).ToList()
            };
        }

        /// <summary>
        /// Creates and instance of <see cref="ObservedGraphInputs"/> from <see cref="PipGraphInputDescriptor"/> instance.
        /// </summary>
        public static ObservedGraphInputs FromPipGraphInputDescriptor(PathTable pathTable, PipGraphInputDescriptor graphInputDescriptor)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(graphInputDescriptor != null);

            var sortedPathInputs =
                SortedReadOnlyArray<GraphPathInput, GraphPathInput.ByPathAndKindComparer>.FromSortedArrayUnsafe(
                    graphInputDescriptor.ObservedInputsSortedByPath.Select(i => GraphPathInput.FromStringKeyedHashObservedInput(pathTable, i))
                        .ToReadOnlyArray(),
                    new GraphPathInput.ByPathAndKindComparer(pathTable.ExpandedPathComparer));

            var sortedEnvironmentVariables = SortedReadOnlyArray<EnvironmentVariableInput, EnvironmentVariableInput.ByNameComparer>.FromSortedArrayUnsafe(
                graphInputDescriptor.EnvironmentVariablesSortedByName.Select(kvp => new EnvironmentVariableInput(kvp.Key, kvp.Value))
                    .ToReadOnlyArray(),
                EnvironmentVariableInput.ByNameComparer.Instance);

            var sortedMounts = SortedReadOnlyArray<MountInput, MountInput.ByNameComparer>.FromSortedArrayUnsafe(
                graphInputDescriptor.MountsSortedByName.Select(i => MountInput.FromStringKeyValue(pathTable, i))
                    .ToReadOnlyArray(),
                MountInput.ByNameComparer.Instance);

            return new ObservedGraphInputs(sortedPathInputs, sortedEnvironmentVariables, sortedMounts);
        }

        /// <summary>
        /// Creates an empty <see cref="ObservedGraphInputs"/>.
        /// </summary>
        public static ObservedGraphInputs CreateEmpty(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            var sortedPathInputs =
                SortedReadOnlyArray<GraphPathInput, GraphPathInput.ByPathAndKindComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<GraphPathInput>.Empty,
                    new GraphPathInput.ByPathAndKindComparer(pathTable.ExpandedPathComparer));

            var sortedEnvironmentVariables =
                SortedReadOnlyArray<EnvironmentVariableInput, EnvironmentVariableInput.ByNameComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<EnvironmentVariableInput>.Empty,
                    EnvironmentVariableInput.ByNameComparer.Instance);

            var sortedMounts =
                SortedReadOnlyArray<MountInput, MountInput.ByNameComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<MountInput>.Empty,
                    MountInput.ByNameComparer.Instance);

            return new ObservedGraphInputs(sortedPathInputs, sortedEnvironmentVariables, sortedMounts);
        }
    }

    /// <summary>
    /// Path input for graph.
    /// </summary>
    public readonly struct GraphPathInput : IEquatable<GraphPathInput>
    {
        /// <summary>
        /// Comparer for <see cref="GraphPathInput" /> that only compares the path element.
        /// </summary>
        public class ByPathAndKindComparer : IComparer<GraphPathInput>
        {
            /// <summary>
            /// Path comparer.
            /// </summary>
            public readonly PathTable.ExpandedAbsolutePathComparer PathComparer;

            /// <summary>
            /// Creates an instance of <see cref="ByPathAndKindComparer" />.
            /// </summary>
            public ByPathAndKindComparer(PathTable.ExpandedAbsolutePathComparer pathComparer)
            {
                Contract.Requires(pathComparer != null);
                PathComparer = pathComparer;
            }

            /// <inheritdoc />
            public int Compare(GraphPathInput x, GraphPathInput y)
            {
                var pathCompare = PathComparer.Compare(x.Path, y.Path);
                return pathCompare != 0 ? pathCompare : (x.DirectoryMembership ? 1 : 0).CompareTo(y.DirectoryMembership ? 1 : 0);
            }
        }

        /// <summary>
        /// Path.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Hash value.
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// Flag indicating if this input is about directory membership.
        /// </summary>
        public readonly bool DirectoryMembership;

        /// <summary>
        /// Creates an instance of <see cref="GraphPathInput" />.
        /// </summary>
        public GraphPathInput(AbsolutePath path, ContentHash hash, bool directoryMembership)
        {
            Path = path;
            Hash = hash;
            DirectoryMembership = directoryMembership;
        }

        /// <inheritdoc />
        public bool Equals(GraphPathInput other)
        {
            return Path == other.Path && DirectoryMembership == other.DirectoryMembership && Hash == other.Hash;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), Hash.GetHashCode(), (DirectoryMembership ? 1 : 0).GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(GraphPathInput left, GraphPathInput right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(GraphPathInput left, GraphPathInput right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Translates to <see cref="StringKeyedHashObservedInput"/>.
        /// </summary>
        public StringKeyedHashObservedInput ToStringKeyedHashObservedInput(PathTable pathTable)
        {
            return new StringKeyedHashObservedInput
                   {
                       StringKeyedHash = new StringKeyedHash
                                         {
                                             Key = Path.ToString(pathTable),
                                             ContentHash = Hash.ToBondContentHash(),
                                         },
                       ObservedInputKind = DirectoryMembership ? ObservedInputKind.DirectoryMembership : ObservedInputKind.ObservedInput,
                   };
        }

        /// <summary>
        /// Creates an instance of <see cref="GraphPathInput"/> from <see cref="StringKeyedHashObservedInput"/>.
        /// </summary>
        public static GraphPathInput FromStringKeyedHashObservedInput(PathTable pathTable, StringKeyedHashObservedInput observedInput)
        {
            return new GraphPathInput(
                AbsolutePath.Create(pathTable, observedInput.StringKeyedHash.Key),
                observedInput.StringKeyedHash.ContentHash.ToContentHash(),
                observedInput.ObservedInputKind == ObservedInputKind.DirectoryMembership);
        }
    }

    /// <summary>
    /// Environment variable input for graph.
    /// </summary>
    public readonly struct EnvironmentVariableInput : IEquatable<EnvironmentVariableInput>
    {
        /// <summary>
        /// Comparer for <see cref="EnvironmentVariableInput"/> that compares just the environment variable name.
        /// </summary>
        public class ByNameComparer : IComparer<EnvironmentVariableInput>
        {
            /// <summary>
            /// Singleton instance.
            /// </summary>
            public static readonly ByNameComparer Instance = new ByNameComparer();

            /// <inheritdoc />
            public int Compare(EnvironmentVariableInput x, EnvironmentVariableInput y)
            {
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Environment variable name.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Environment variable value.
        /// </summary>
        [CanBeNull]
        public readonly string Value;

        /// <summary>
        /// Creates an instance of <see cref="EnvironmentVariableInput"/>.
        /// </summary>
        public EnvironmentVariableInput(string name, [CanBeNull] string value)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            Name = name;
            Value = value;
        }

        /// <inheritdoc />
        public bool Equals(EnvironmentVariableInput other)
        {
            return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name.GetHashCode(), Value?.GetHashCode() ?? 0);
        }

        /// <nodoc />
        public static bool operator ==(EnvironmentVariableInput left, EnvironmentVariableInput right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(EnvironmentVariableInput left, EnvironmentVariableInput right)
        {
            return !(left == right);
        }
    }

    /// <summary>
    /// Mount input for graph.
    /// </summary>
    public readonly struct MountInput : IEquatable<MountInput>
    {
        /// <summary>
        /// Comparer for <see cref="MountInput"/> that compares just the mount name.
        /// </summary>
        public class ByNameComparer : IComparer<MountInput>
        {
            /// <summary>
            /// Singleton instance.
            /// </summary>
            public static readonly ByNameComparer Instance = new ByNameComparer();

            /// <inheritdoc />
            public int Compare(MountInput x, MountInput y)
            {
                return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Mount name.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Mount path value.
        /// </summary>
        public readonly AbsolutePath Path;

        /// <summary>
        /// Creates an instance of <see cref="MountInput"/>.
        /// </summary>
        public MountInput(string name, AbsolutePath path)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));

            Name = name;
            Path = path;
        }

        /// <inheritdoc />
        public bool Equals(MountInput other) => string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) && Path == other.Path;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name.GetHashCode(), Path.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(MountInput left, MountInput right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(MountInput left, MountInput right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Translates to <see cref="StringKeyValue"/>.
        /// </summary>
        public StringKeyValue ToStringKeyValue(PathTable pathTable)
        {
            return new StringKeyValue
            {
                Key = Name,
                Value = Path.IsValid ? Path.ToString(pathTable) : null
            };
        }

        /// <summary>
        /// Creates an instance of <see cref="MountInput"/> from <see cref="StringKeyValue"/>.
        /// </summary>
        public static MountInput FromStringKeyValue(PathTable pathTable, StringKeyValue observedInput)
        {
            return new MountInput(
                observedInput.Key, 
                observedInput.Value == null ? AbsolutePath.Invalid : AbsolutePath.Create(pathTable, observedInput.Value));
        }
    }
}
