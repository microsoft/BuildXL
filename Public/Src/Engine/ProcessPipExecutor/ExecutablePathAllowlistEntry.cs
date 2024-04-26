// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using System;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// A file-access allowlist rule that will match on tool and path.
    /// </summary>
    public sealed class ExecutablePathAllowlistEntry : FileAccessAllowlistEntry
    {
        /// <summary>
        /// Absolute path or executable name of the tool that is allowed to perform this access.
        /// It is not mandatory to provide the toolPath.
        /// </summary>
        public readonly DiscriminatingUnion<AbsolutePath, PathAtom> Executable;

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and path.
        /// </summary>
        /// <param name="executable">The exact full path or the executable name to the tool that does the bad access. Can be optional.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(DiscriminatingUnion<AbsolutePath, PathAtom> executable, SerializableRegex pathRegex, bool allowsCaching, string name)
            : base(pathRegex, allowsCaching, name)
        {
            Contract.Requires(pathRegex != null);

            Executable = executable;
        }

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and full path
        /// </summary>
        /// <param name="executablePath">The exact full path to the tool that does the bad access. Can be optional.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(AbsolutePath executablePath, SerializableRegex pathRegex, bool allowsCaching, string name)
            : this(new DiscriminatingUnion<AbsolutePath, PathAtom>(executablePath), pathRegex, allowsCaching, name)
        {
        }

        /// <summary>
        /// Construct a new allowlist entry that will match based on tool and full path
        /// </summary>
        /// <param name="executableName">The executable name of the tool that does the bad access. Can be optional.</param>
        /// <param name="pathRegex">The ECMAScript regex pattern that will be used as the basis for the match.</param>
        /// <param name="allowsCaching">
        /// Whether this allowlist rule should be interpreted to allow caching of a pip that matches
        /// it.
        /// </param>
        /// <param name="name">Name of the allowlist entry. Defaults to 'Unnamed' if null/empty.</param>
        public ExecutablePathAllowlistEntry(PathAtom executableName, SerializableRegex pathRegex, bool allowsCaching, string name)
            : this(new DiscriminatingUnion<AbsolutePath, PathAtom>(executableName), pathRegex, allowsCaching, name)
        {
        }

        /// <inheritdoc />
        public override FileAccessAllowlist.MatchType Matches(ReportedFileAccess reportedFileAccess, Process pip, PathTable pathTable)
        {
            Contract.Requires(pip != null);
            Contract.Requires(pathTable != null);

            // An access is allowlisted if:
            // * The tool was in the allowlist (implicit here by lookup from FileAccessAllowlist.Matches), if specified AND
            // * the path filter matches (or is empty).
            // * It is also allowed if the toolPath is not present but the PathRegex matches.
            return FileAccessAllowlist.Match(FileAccessAllowlist.PathFilterMatches(PathRegex.Regex, reportedFileAccess, pathTable), AllowsCaching);
        }

        /// <nodoc />
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            WriteState(writer);

            object executablePathType = Executable?.GetValue();
            // Determine the type of the executable value and write appropriate executablePath to the writer.
            // We use specific byte flags to encode the type of executablePath that follows:
            // 0 -> null: Indicates the absence of a value.
            // 1 -> AbsolutePath: Indicates the value is of type AbsolutePath, which will be serialized next.
            // 2 -> PathAtom: Indicates the value is of type PathAtom, which will be serialized next.
            switch (executablePathType)
            {
                case null:
                    writer.Write((byte)0);
                    break;
                case AbsolutePath absolutePath:
                    writer.Write((byte)1);
                    writer.Write(absolutePath);
                    break;
                case PathAtom pathAtom:
                    writer.Write((byte)2);
                    writer.Write(pathAtom);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected value for executablePathType: {executablePathType}");

            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ExecutablePathAllowlistEntry Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var state = ReadState(reader);
            var executablePathType = reader.ReadByte();
            DiscriminatingUnion<AbsolutePath, PathAtom> executable = null;

            switch (executablePathType)
            {
                case 0:
                    executable = null;
                    break;
                case 1:
                    AbsolutePath absolutePath = reader.ReadAbsolutePath();
                    executable = new DiscriminatingUnion<AbsolutePath, PathAtom>(absolutePath);
                    break;
                case 2:
                    PathAtom pathAtom = reader.ReadPathAtom();
                    executable = new DiscriminatingUnion<AbsolutePath, PathAtom>(pathAtom);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected value for executablePathType: {executablePathType}");
            }

            return new ExecutablePathAllowlistEntry(executable, state.PathRegex, state.AllowsCaching, state.Name);
        }
    }
}
