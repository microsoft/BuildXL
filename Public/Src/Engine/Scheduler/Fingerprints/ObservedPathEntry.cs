// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Fingerprints
{
    [Flags]
    internal enum ObservedPathEntryFlags : byte
    {
        None = 0,
        IsSearchPath = 1,
        IsDirectoryPath = 2,
        DirectoryEnumeration = 4,
        DirectoryEnumerationWithCustomPattern = 8,
        DirectoryEnumerationWithAllPattern = 16,
        FileProbe = 32
    }

    /// <summary>
    /// String constants to keep labels consistent.
    /// </summary>
    public readonly struct ObservedPathEntryConstants
    {
        /// <summary>
        /// Label for <see cref="ObservedPathEntry.Path"/>.
        /// </summary>
        public const string Path = "Path";

        /// <summary>
        /// Label for <see cref="ObservedPathEntry.Flags"/>.
        /// </summary>
        public const string Flags = "Flags";

        /// <summary>
        /// Label for <see cref="ObservedPathEntry.EnumeratePatternRegex"/>.
        /// </summary>
        public const string EnumeratePatternRegex = "EnumeratePatternRegex";

        /// <summary>
        /// Hashing label for <see cref="ObservedPathSet"/>.
        /// </summary>
        public const string PathSet = "PathSet";
    }

    /// <summary>
    /// Represents a path in an <see cref="ObservedPathSet"/> and associated data.
    /// </summary>
    public readonly struct ObservedPathEntry : IEquatable<ObservedPathEntry>
    {
        public readonly AbsolutePath Path;
        public readonly string EnumeratePatternRegex;
        internal readonly ObservedPathEntryFlags Flags;

        public bool IsSearchPath => (Flags & ObservedPathEntryFlags.IsSearchPath) != 0;

        public bool IsDirectoryPath => (Flags & ObservedPathEntryFlags.IsDirectoryPath) != 0;

        public bool DirectoryEnumeration => (Flags & ObservedPathEntryFlags.DirectoryEnumeration) != 0;

        public bool DirectoryEnumerationWithCustomPattern => (Flags & ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern) != 0;

        public bool DirectoryEnumerationWithAllPattern => (Flags & ObservedPathEntryFlags.DirectoryEnumerationWithAllPattern) != 0;

        public bool IsFileProbe => (Flags & ObservedPathEntryFlags.FileProbe) != 0;

        public ObservedPathEntry(AbsolutePath path, bool isSearchPathEnumeration, bool isDirectoryPath, bool isDirectoryEnumeration, string enumeratePatternRegex, bool isFileProbe)
        {
            Contract.Requires(path.IsValid);

            Path = path;
            Flags = ObservedPathEntryFlags.None;
            Flags |= isFileProbe ? ObservedPathEntryFlags.FileProbe : ObservedPathEntryFlags.None;
            Flags |= isSearchPathEnumeration ? ObservedPathEntryFlags.IsSearchPath : ObservedPathEntryFlags.None;
            Flags |= isDirectoryPath ? ObservedPathEntryFlags.IsDirectoryPath : ObservedPathEntryFlags.None;
            Flags |= isDirectoryEnumeration ? ObservedPathEntryFlags.DirectoryEnumeration : ObservedPathEntryFlags.None;
            if (enumeratePatternRegex != null)
            {
                Flags |= string.Equals(enumeratePatternRegex, RegexDirectoryMembershipFilter.AllowAllRegex, StringComparison.OrdinalIgnoreCase) ? ObservedPathEntryFlags.DirectoryEnumerationWithAllPattern : ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern;
            }

            EnumeratePatternRegex = enumeratePatternRegex;
        }

        internal ObservedPathEntry(AbsolutePath path, ObservedPathEntryFlags flags, string enumeratePatternRegex)
        {
            Contract.Requires(path.IsValid);

            Path = path;
            Flags = flags;
            EnumeratePatternRegex = enumeratePatternRegex;
        }

        /// <summary>
        /// Converts from the given ObservedInput to an <see cref="ObservedPathEntry"/>
        /// </summary>
        public static ObservedPathEntry FromObservedInput(ObservedInput input)
        {
            return input.PathEntry;
        }

        /// <nodoc />
        public static bool operator ==(ObservedPathEntry left, ObservedPathEntry right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ObservedPathEntry left, ObservedPathEntry right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(ObservedPathEntry other)
        {
            return other.Path == Path && other.Flags == Flags;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Path.GetHashCode(), Flags.GetHashCode());
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Path);
            writer.Write((byte)Flags);
            if (DirectoryEnumerationWithCustomPattern)
            {
                writer.Write(EnumeratePatternRegex);
            }
        }

        public static ObservedPathEntry Deserialize(BuildXLReader reader)
        {
            AbsolutePath path = reader.ReadAbsolutePath();
            ObservedPathEntryFlags flags = (ObservedPathEntryFlags)reader.ReadByte();
            string enumeratePatternRegex = null;
            if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithCustomPattern) != 0)
            {
                enumeratePatternRegex = reader.ReadString();
            }
            else if ((flags & ObservedPathEntryFlags.DirectoryEnumerationWithAllPattern) != 0)
            {
                enumeratePatternRegex = RegexDirectoryMembershipFilter.AllowAllRegex;
            }

            return new ObservedPathEntry(path, flags, enumeratePatternRegex);
        }
    }
}
