// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Processes
{
    /// <summary>
    /// A file-access allowlist rule that will match on tool and path.
    /// </summary>
    public sealed class ExecutablePathAllowlistEntry : FileAccessAllowlistEntry
    {
        /// <summary>
        /// Absolute path or executable name of the tool that is allowed to perform this access.
        /// </summary>
        public readonly DiscriminatingUnion<AbsolutePath, PathAtom> Executable;

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and path.
        /// </summary>
        /// <param name="executable">The exact full path or the executable name to the tool that does the bad access.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(DiscriminatingUnion<AbsolutePath, PathAtom> executable, SerializableRegex pathRegex, bool allowsCaching, string name)
            : base(pathRegex, allowsCaching, name)
        {
            Contract.RequiresNotNull(executable);
            Contract.Requires(pathRegex != null);

            Executable = executable;
        }

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and full path
        /// </summary>
        /// <param name="executablePath">The exact full path to the tool that does the bad access.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(AbsolutePath executablePath, SerializableRegex pathRegex, bool allowsCaching, string name)
            : this(new DiscriminatingUnion<AbsolutePath, PathAtom>(executablePath), pathRegex, allowsCaching, name)
        {
            Contract.Requires(executablePath.IsValid);
        }

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and full path
        /// </summary>
        /// <param name="executableName">The executable name of the tool that does the bad access.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(PathAtom executableName, SerializableRegex pathRegex, bool allowsCaching, string name)
            : this(new DiscriminatingUnion<AbsolutePath, PathAtom>(executableName), pathRegex, allowsCaching, name)
        {
            Contract.Requires(executableName.IsValid);
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
            object executableValue = Executable.GetValue();
            if (executableValue is AbsolutePath absolutePath)
            {
                writer.Write(true);
                writer.Write(absolutePath);
            }
            else if (executableValue is PathAtom pathAtom)
            {
                writer.Write(false);
                writer.Write(pathAtom);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ExecutablePathAllowlistEntry Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var state = ReadState(reader);
            var isAbsolutePath = reader.ReadBoolean();
            ExecutablePathAllowlistEntry listEntry;

            if (isAbsolutePath)
            {
                AbsolutePath path = reader.ReadAbsolutePath();
                listEntry = new ExecutablePathAllowlistEntry(
                                    new DiscriminatingUnion<AbsolutePath, PathAtom>(path),
                                    state.PathRegex,
                                    state.AllowsCaching,
                                    state.Name);
            }
            else
            {
                PathAtom path = reader.ReadPathAtom();
                listEntry = new ExecutablePathAllowlistEntry(
                                    new DiscriminatingUnion<AbsolutePath, PathAtom>(path),
                                    state.PathRegex,
                                    state.AllowsCaching,
                                    state.Name);
            }
            
            return listEntry;
        }
    }
}
