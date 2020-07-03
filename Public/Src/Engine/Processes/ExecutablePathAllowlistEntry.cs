// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// A file-access allowlist rule that will match on tool and path.
    /// </summary>
    public sealed class ExecutablePathAllowlistEntry : FileAccessAllowlistEntry
    {
        private readonly AbsolutePath m_executablePath;

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and path.
        /// </summary>
        /// <param name="executablePath">The exact full path to the tool that does the bad access.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(AbsolutePath executablePath, SerializableRegex pathRegex, bool allowsCaching, string name)
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
        public override FileAccessAllowlist.MatchType Matches(ReportedFileAccess reportedFileAccess, Process pip, PathTable pathTable)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            // An access is allowlisted if:
            // * The tool was in the allowlist (implicit here by lookup from FileAccessAllowlist.Matches) AND
            // * the path filter matches (or is empty)
            return FileAccessAllowlist.Match(FileAccessAllowlist.PathFilterMatches(PathRegex.Regex, reportedFileAccess, pathTable), AllowsCaching);
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
        public static ExecutablePathAllowlistEntry Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var state = ReadState(reader);
            AbsolutePath path = reader.ReadAbsolutePath();

            return new ExecutablePathAllowlistEntry(
                path,
                state.PathRegex,
                state.AllowsCaching,
                state.Name);
        }
    }
}
