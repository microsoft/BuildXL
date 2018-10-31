// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A file-access whitelist rule that will match on tool and path.
    /// </summary>
    public sealed class ExecutablePathWhitelistEntry : FileAccessWhitelistEntry
    {
        private readonly AbsolutePath m_executablePath;

        /// <summary>
        /// Construct a new whitelist entry that will match based on tool and path.
        /// </summary>
        /// <param name="executablePath">The exact full path to the tool that does the bad access.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this whitelist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the whitelist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathWhitelistEntry(AbsolutePath executablePath, SerializableRegex pathRegex, bool allowsCaching, string name)
            : base(pathRegex, allowsCaching, name)
        {
            Contract.Requires(executablePath.IsValid);
            Contract.Requires(pathRegex != null);

            m_executablePath = executablePath;
        }

        /// <summary>
        /// Absolute path of the tool that is allowed to perform this access.
        /// </summary>
        public AbsolutePath ExecutablePath
        {
            get { return m_executablePath; }
        }

        /// <inheritdoc />
        public override FileAccessWhitelist.MatchType Matches(ReportedFileAccess reportedFileAccess, Process pip, PathTable pathTable)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            // An access is whitelisted if:
            // * The tool was in the whitelist (implicit here by lookup from FileAccessWhitelist.Matches) AND
            // * the path filter matches (or is empty)
            return FileAccessWhitelist.Match(FileAccessWhitelist.PathFilterMatches(PathRegex.Regex, reportedFileAccess, pathTable), AllowsCaching);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            WriteState(writer);
            writer.Write(m_executablePath);
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ExecutablePathWhitelistEntry Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var state = ReadState(reader);
            AbsolutePath path = reader.ReadAbsolutePath();

            return new ExecutablePathWhitelistEntry(
                path,
                state.PathRegex,
                state.AllowsCaching,
                state.Name);
        }
    }
}
