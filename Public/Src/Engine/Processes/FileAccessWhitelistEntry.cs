// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using JetBrains.Annotations;
using NotNull = JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.Processes
{
    /// <summary>
    /// A rule that can determine whether a given file access is whitelisted.
    /// </summary>
    public abstract class FileAccessWhitelistEntry
    {
        /// <summary>
        /// Determines whether a pip execution with *only* this violation (and other similarly-whitelisted violations) should be cached.
        /// </summary>
        public bool AllowsCaching { get; }

        /// <summary>
        /// The underlying Regex. Internal for testing
        /// </summary>
        internal SerializableRegex PathRegex { get; }

        /// <summary>
        /// The name of the whitelist rule
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Protected constructor
        /// </summary>
        protected FileAccessWhitelistEntry([NotNull]SerializableRegex pathRegex, bool allowsCaching, string name)
        {
            Contract.Requires(pathRegex != null);

            PathRegex = pathRegex;
            AllowsCaching = allowsCaching;
            Name = string.IsNullOrEmpty(name) ? "Unnamed" : name;
        }

        /// <summary>
        /// Determine whether a ReportedFileAccess matches the whitelist rules.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011", Justification = "Only a Process can have unknown file accesses.")]
        public abstract FileAccessWhitelist.MatchType Matches(ReportedFileAccess reportedFileAccess, [NotNull]Process pip, [NotNull]PathTable pathTable);

        #region Serialization

        /// <summary>
        /// Writes the state
        /// </summary>
        protected void WriteState(BinaryWriter writer)
        {
            WriteState(
                writer,
                new SerializationState()
                {
                    AllowsCaching = AllowsCaching,
                    PathRegex = PathRegex,
                    Name = Name,
                });
        }

        private static void WriteState(BinaryWriter writer, SerializationState state)
        {
            writer.Write(state.AllowsCaching);
            state.PathRegex.Write(writer);
            writer.Write(state.Name);
        }

        /// <summary>
        /// Reads the serialization state
        /// </summary>
        protected static SerializationState ReadState(BinaryReader reader)
        {
            SerializationState state = default(SerializationState);
            state.AllowsCaching = reader.ReadBoolean();
            state.PathRegex = SerializableRegex.Read(reader);
            state.Name = reader.ReadString();

            return state;
        }

        /// <summary>
        /// State that is serialized for the FileAccessWhitelistEntry
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
        protected struct SerializationState
        {
            /// <summary>
            /// Path Regex
            /// </summary>
            public SerializableRegex PathRegex;

            /// <summary>
            /// Whether to allow caching of the entry
            /// </summary>
            public bool AllowsCaching;

            /// <summary>
            /// Name of the whitelist entry
            /// </summary>
            public string Name;
        }

        #endregion
    }
}
