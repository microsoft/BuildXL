// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Data to be included in <see cref="SandboxedProcessInfo"/> for remote execution.
    /// </summary>
    public class RemoteSandboxedProcessData
    {
        /// <summary>
        /// Temporary directories that need to be created prior to pip execution on remote worker/agent.
        /// </summary>
        /// <remarks>
        /// Pips often assume that temp directories exist prior to their executions. Since pips should not share
        /// temp directories, they should not be brought from the client. Thus, they have to be created before
        /// pips execute.
        /// 
        /// One should not include the output directories to be pre-created because that will prevent the ProjFs
        /// on agent to have a callback to the client. For example, a pip A can write an output D/f.txt which will be
        /// consumed by a pip B, and in turn, B produces D/g.txt. If D is pre-created on agent before B executes, then
        /// B will not find D/f.txt because ProjFs already see D as materialized.
        /// </remarks>
        public readonly IReadOnlyList<string> TempDirectories;

        /// <summary>
        /// Untracked scopes.
        /// </summary>
        /// <remarks>
        /// This is experimental. This data is needed to filter inputs/outputs observed by AnyBuild, so that, particularly for outputs,
        /// AnyBuild does not try to send them back to the client. We introduced this data to handle OACR pips, where they have shared accesses
        /// on the same untracked output. When running locally, those pips succeed, but when they run remotely in isolation, they fail because
        /// the output has incorrect content.
        /// 
        /// TODO: Since we will run OACR pips without sharing, this data may no longer be needed.
        /// </remarks>
        public readonly IReadOnlySet<string> UntrackedScopes;

        /// <summary>
        /// Untracked paths.
        /// </summary>
        /// <remarks>
        /// See remarks on <see cref="UntrackedScopes"/>.
        /// </remarks>
        public readonly IReadOnlySet<string> UntrackedPaths;

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcessData"/>.
        /// </summary>
        /// <param name="tempDirectories">List of temp directories.</param>
        /// <param name="untrackedScopes">Untracked scopes.</param>
        /// <param name="untrackedPaths">Untracked paths.</param>
        public RemoteSandboxedProcessData(
            IReadOnlyList<string> tempDirectories,
            IReadOnlySet<string> untrackedScopes,
            IReadOnlySet<string> untrackedPaths)
        {
            Contract.Requires(tempDirectories != null);
            Contract.Requires(untrackedScopes != null);
            Contract.Requires(untrackedPaths != null);

            TempDirectories = tempDirectories;
            UntrackedScopes = untrackedScopes;
            UntrackedPaths = untrackedPaths;
        }

        /// <summary>
        /// Serializes this instance using an instance of <see cref="BuildXLWriter"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteReadOnlyList(TempDirectories, (w, s) => w.Write(s));
            writer.Write(UntrackedScopes, (w, s) => w.Write(s));
            writer.Write(UntrackedPaths, (w, s) => w.Write(s));
        }

        /// <summary>
        /// Deserializes an instance of <see cref="RemoteSandboxedProcessData"/> using an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static RemoteSandboxedProcessData Deserialize(BuildXLReader reader) =>
            new (reader.ReadReadOnlyList(r => r.ReadString()),
                 reader.ReadReadOnlySet(r => r.ReadString()),
                 reader.ReadReadOnlySet(r => r.ReadString()));

        /// <summary>
        /// Checks if a file access path is untracked.
        /// </summary>
        public bool IsUntracked(string fileAccessPath)
        {
            string normalizedPath = NormalizedInputPathOrNull(fileAccessPath);

            if (normalizedPath == null)
            {
                return false;
            }

            if (UntrackedPaths.Contains(normalizedPath))
            {
                return true;
            }

            foreach (string scope in UntrackedScopes)
            {
                if (IsWithin(scope, normalizedPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizedInputPathOrNull(string path)
        {
            if (path == null || path.Length == 0 || path.StartsWith(@"\\.\", StringComparison.Ordinal))
            {
                return null;
            }

            int skipStart = 0;
            int skipEnd = 0;
            if (path.StartsWith(@"\??\", StringComparison.Ordinal)
                || path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                skipStart = 4;
            }

            if (path[path.Length - 1] == Path.DirectorySeparatorChar)
            {
                skipEnd = 1;
            }
            else if (path.EndsWith(@"\..", StringComparison.Ordinal))
            {
                skipEnd = 3;
            }
            else if (path.EndsWith(@"\.", StringComparison.Ordinal))
            {
                skipEnd = 2;
            }

            int len = path.Length - skipEnd - skipStart;
            if (len < 4)
            {
                // Just a drive letter and colon, or "c:\" which is similar in result.
                return null;
            }

            return path.Substring(skipStart, len);
        }

        private static bool IsWithin(string parentDir, string path)
        {
            if (!path.StartsWith(parentDir, OperatingSystemHelper.PathComparison))
            {
                return false;
            }

            if (path.Length > parentDir.Length && path[parentDir.Length] != Path.DirectorySeparatorChar)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Builder for <see cref="RemoteSandboxedProcessData"/>.
        /// </summary>
        public class Builder
        {
            private readonly ReadOnlyHashSet<string> m_untrackedScopes = new (OperatingSystemHelper.PathComparer);
            private readonly ReadOnlyHashSet<string> m_untrackedPaths = new (OperatingSystemHelper.PathComparer);
            private readonly HashSet<string> m_tempDirectories = new (OperatingSystemHelper.PathComparer);
            private readonly PathTable m_pathTable;

            /// <summary>
            /// Constructor.
            /// </summary>
            public Builder(PathTable pathTable) => m_pathTable = pathTable;

            /// <summary>
            /// Adds an untracked scope.
            /// </summary>
            public void AddUntrackedScope(AbsolutePath path) => m_untrackedScopes.Add(path.ToString(m_pathTable));

            /// <summary>
            /// Adds an untracked path.
            /// </summary>
            public void AddUntrackedPath(AbsolutePath path) => m_untrackedPaths.Add(path.ToString(m_pathTable));

            /// <summary>
            /// Adds a temp directory.
            /// </summary>
            public void AddTempDirectory(AbsolutePath path) => m_tempDirectories.Add(path.ToString(m_pathTable));

            /// <summary>
            /// Builds an instance of <see cref="RemoteSandboxedProcessData"/>.
            /// </summary>
            public RemoteSandboxedProcessData Build() => new (m_tempDirectories.ToList(), m_untrackedScopes, m_untrackedPaths);
        }
    }
}
