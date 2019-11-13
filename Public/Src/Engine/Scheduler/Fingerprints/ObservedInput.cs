// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler.Fingerprints
{
    /// <summary>
    /// Type of <see cref="ObservedInput" />.
    /// </summary>
    public enum ObservedInputType
    {
        /// <summary>
        /// A path was probed, but did not exist.
        /// </summary>
        AbsentPathProbe,

        /// <summary>
        /// A file with known contents was read.
        /// </summary>
        FileContentRead,

        /// <summary>
        /// A directory was enumerated (kind of like a directory read).
        /// </summary>
        DirectoryEnumeration,

        /// <summary>
        /// An existing directory probe.
        /// </summary>
        ExistingDirectoryProbe,

        /// <summary>
        /// An existing file probe.
        /// </summary>
        ExistingFileProbe,
    }

    /// <summary>
    /// String constants to keep labels consistent.
    /// </summary>
    public static class ObservedInputConstants
    {
        /// <summary>
        /// Hashing label for <see cref="ObservedInputType.AbsentPathProbe"/>.
        /// </summary>
        public const string AbsentPathProbe = "P";

        /// <summary>
        /// Hashing label for <see cref="ObservedInputType.FileContentRead"/>.
        /// </summary>
        public const string FileContentRead = "R";

        /// <summary>
        /// Hashing label for <see cref="ObservedInputType.DirectoryEnumeration"/>.
        /// </summary>
        public const string DirectoryEnumeration = "E";

        /// <summary>
        /// Hashing label for <see cref="ObservedInputType.ExistingDirectoryProbe"/>.
        /// </summary>
        public const string ExistingDirectoryProbe = "D";

        /// <summary>
        /// Hashing label for <see cref="ObservedInputType.ExistingFileProbe"/>.
        /// </summary>
        public const string ExistingFileProbe = "F";

        /// <summary>
        /// Hashing label for collection of <see cref="ObservedInput"/>
        /// </summary>
        public const string ObservedInputs = "ObservedInputs";

        /// <summary>
        /// Helper function to convert from abbreviated strings to full type strings.
        /// </summary>
        /// <returns>
        /// If the input string is an abbreviated observed input type, the expanded form; otherwise, the input string unaltered.
        /// </returns>
        public static string ToExpandedString(string observedInputConstant)
        {
            switch (observedInputConstant)
            {
                case ObservedInputConstants.AbsentPathProbe:
                    return ObservedInputType.AbsentPathProbe.ToString();
                case ObservedInputConstants.FileContentRead:
                    return ObservedInputType.FileContentRead.ToString();
                case ObservedInputConstants.DirectoryEnumeration:
                    return ObservedInputType.DirectoryEnumeration.ToString();
                case ObservedInputConstants.ExistingDirectoryProbe:
                    return ObservedInputType.ExistingDirectoryProbe.ToString();
                case ObservedInputConstants.ExistingFileProbe:
                    return ObservedInputType.ExistingFileProbe.ToString();
                default:
                    return observedInputConstant;
            }
        }
    }

    /// <summary>
    /// An <see cref="ObservedInput" /> represents a dynamically discovered dependency on some aspect of the filesystem.
    /// The supported types of dependencies are enumerated as <see cref="ObservedInputType" /> (such as <see cref="ObservedInputType.AbsentPathProbe" />).
    /// Versus an <see cref="BuildXL.Processes.ObservedFileAccess" />, this dependency identifies the particular content accessed (e.g. file hash) rather than
    /// the particular low-level filesystem operations used to access it.
    /// One may service <see cref="ObservedInput" />s from a list of accessed paths using an <see cref="ObservedInputProcessor" />, which
    /// applies access rules such that (if successful) the returned dependencies are actually valid for the traced process.
    /// </summary>
    public readonly struct ObservedInput : IEquatable<ObservedInput>
    {
        public readonly ObservedInputType Type;
        public readonly ContentHash Hash;

        internal readonly ObservedPathEntry PathEntry;

        public AbsolutePath Path => PathEntry.Path;

        public bool IsSearchPath => PathEntry.IsSearchPath;

        public bool IsDirectoryPath => PathEntry.IsDirectoryPath;

        public bool DirectoryEnumeration => PathEntry.DirectoryEnumeration;

        private ObservedInput(
            ObservedInputType type,
            AbsolutePath path,
            ContentHash? hash = null,
            bool isSearchPath = false,
            bool isDirectoryPath = false,
            bool directoryEnumeration = false,
            bool isFileProbe = false,
            string enumeratePatternRegex = null)
            :
            this(type, hash, new ObservedPathEntry(path,
                isSearchPathEnumeration: isSearchPath,
                isDirectoryPath: isDirectoryPath,
                isDirectoryEnumeration: directoryEnumeration,
                enumeratePatternRegex: enumeratePatternRegex,
                isFileProbe: isFileProbe))
        {
            Contract.Requires(path.IsValid);
        }

        internal ObservedInput(
            ObservedInputType type,
            ContentHash? hash,
            ObservedPathEntry pathEntry)
        {
            Contract.Requires(hash.HasValue || HasPredefinedHash(type));
            Contract.Requires(hash == null || hash.Value.HashType != HashType.Unknown);

            Type = type;
            Hash = hash ?? GetPredefinedHash(type);
            PathEntry = pathEntry;
        }

        /// <summary>
        /// Creates an input of type <see cref="ObservedInputType.AbsentPathProbe" />
        /// </summary>
        public static ObservedInput CreateAbsentPathProbe(
            AbsolutePath path,
            bool isSearchPath = false,
            bool isDirectoryPath = false,
            bool directoryEnumeration = false,
            string enumeratePatternRegex = null)
        {
            return new ObservedInput(
                ObservedInputType.AbsentPathProbe,
                path: path,
                isSearchPath: isSearchPath,
                isDirectoryPath: isDirectoryPath,
                directoryEnumeration: directoryEnumeration,
                enumeratePatternRegex: enumeratePatternRegex);
        }

        /// <summary>
        /// Creates an input of type <see cref="ObservedInputType.FileContentRead" />
        /// </summary>
        public static ObservedInput CreateFileContentRead(AbsolutePath path, ContentHash fileContent)
        {
            return new ObservedInput(ObservedInputType.FileContentRead, path, fileContent);
        }

        /// <summary>
        /// Creates an input of type <see cref="ObservedInputType.DirectoryEnumeration" />
        /// </summary>
        public static ObservedInput CreateDirectoryEnumeration(
            AbsolutePath path, 
            DirectoryFingerprint fingerprint, 
            bool isSearchPath = false, 
            string enumeratePatternRegex = null)
        {
            return new ObservedInput(
                ObservedInputType.DirectoryEnumeration, 
                path, 
                fingerprint.Hash, 
                isSearchPath: isSearchPath, 
                isDirectoryPath: true, 
                directoryEnumeration: true, 
                enumeratePatternRegex: enumeratePatternRegex);
        }

        /// <summary>
        /// Creates an input of type <see cref="ObservedInputType.ExistingFileProbe" />
        /// </summary>
        public static ObservedInput CreateExistingFileProbe(AbsolutePath path)
        {
            return new ObservedInput(ObservedInputType.ExistingFileProbe, path, isFileProbe: true);
        }

        /// <summary>
        /// Creates an input of type <see cref="ObservedInputType.ExistingDirectoryProbe" />
        /// </summary>
        public static ObservedInput CreateExistingDirectoryProbe(AbsolutePath path)
        {
            return new ObservedInput(ObservedInputType.ExistingDirectoryProbe, path, isDirectoryPath: true);
        }

        /// <nodoc />
        public static bool operator ==(ObservedInput left, ObservedInput right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ObservedInput left, ObservedInput right)
        {
            return !left.Equals(right);
        }

        /// <inheritdoc />
        public bool Equals(ObservedInput other)
        {
            return other.Type == Type && other.Hash == Hash && PathEntry.Equals(other.PathEntry);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Type.GetHashCode(), Hash.GetHashCode(), PathEntry.GetHashCode());
        }

        /// <summary>
        /// Gets the predefined hash for the observed input type. This should only be called if
        /// <see cref="HasPredefinedHash(ObservedInputType)"/> returns true.
        /// </summary>
        public static ContentHash GetPredefinedHash(ObservedInputType type)
        {
            Contract.Requires(HasPredefinedHash(type), "ObservedInput type does not have predefined hash");

            switch (type)
            {
                case ObservedInputType.AbsentPathProbe:
                    return WellKnownContentHashes.AbsentFile;
                case ObservedInputType.ExistingDirectoryProbe:
                case ObservedInputType.ExistingFileProbe:
                    return ContentHashingUtilities.ZeroHash;
                default:
                    throw Contract.AssertFailure("Unexpected ObservedInputType");
            }
        }

        /// <summary>
        /// Gets whether the observed input type represents a type which has a predefined hash
        /// </summary>
        [Pure]
        public static bool HasPredefinedHash(ObservedInputType type)
        {
            switch (type)
            {
                case ObservedInputType.AbsentPathProbe:
                case ObservedInputType.ExistingDirectoryProbe:
                case ObservedInputType.ExistingFileProbe:
                    return true;
                case ObservedInputType.FileContentRead:
                case ObservedInputType.DirectoryEnumeration:
                    return false;
                default:
                    throw Contract.AssertFailure("Unknown ObservedInputType");
            }
        }

        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact((int)Type);
            PathEntry.Serialize(writer);
            if (!HasPredefinedHash(Type))
            {
                Hash.SerializeHashBytes(writer);
            }
        }

        public static ObservedInput Deserialize(BuildXLReader reader)
        {
            ObservedInputType type = (ObservedInputType)reader.ReadInt32Compact();
            ObservedPathEntry pathEntry = ObservedPathEntry.Deserialize(reader);
            ContentHash? hash = HasPredefinedHash(type)
                ? (ContentHash?) null
                : ContentHashingUtilities.CreateFrom(reader); // don't specific explicit hash for predefined hash input types
            return new ObservedInput(type, hash, pathEntry);
        }
    }
}
