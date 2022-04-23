// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Data to be included in <see cref="SandboxedProcessInfo"/> for remote execution.
    /// </summary>
    public class RemoteSandboxedProcessData
    {
        /// <summary>
        /// Tagging for backward/forward-compat serialization.
        /// </summary>
        /// <remarks>
        /// This tagging mechanism is an oversimplified version of what Protobuf does.
        /// The need for tagging is to support backward/forward compatibility in serialization.
        /// An instance of <see cref="RemoteSandboxedProcessData"/> produced by BuildXL will be
        /// read by the remoting tool, e.g., AnyBuild, to optimize process remoting, e.g., peforming
        /// file/directory predictions. Since that remoting tool may have a different BuildXL.Process.dll
        /// than what is used by BuildXL, then there's a risk that the serialized data cannot be read by
        /// the remoting tool. For compatibility sake, remoting data is serialized by tagging each of
        /// its entries.
        /// 
        /// In the future we may think of using protobuf-net https://github.com/protobuf-net/protobuf-net.
        /// For now, this simplified version of Protobuf suffices for our remoting purpose.
        /// </remarks>
        private static class DataTag
        {
            // Tag 0 is reserved.

            /// <nodoc/>
            public const byte Executable = 1;

            /// <nodoc/>
            public const byte Arguments = 2;

            /// <nodoc/>
            public const byte WorkingDirectory = 3;

            /// <nodoc/>
            public const byte EnvVars = 4;

            /// <nodoc/>
            public const byte FileDependency = 5;

            /// <nodoc/>
            public const byte DirectoryDependency = 6;

            /// <nodoc/>
            public const byte OutputDirectory = 7;

            /// <nodoc/>
            public const byte TempDirectory = 8;

            /// <nodoc/>
            public const byte UntrackedScope = 9;

            /// <nodoc/>
            public const byte UntrackedPath = 10;
        }

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
        public readonly IReadOnlySet<string> TempDirectories;

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
        /// File dependencies.
        /// </summary>
        public readonly IReadOnlySet<string> FileDependencies;

        /// <summary>
        /// Directory dependencies.
        /// </summary>
        public readonly IReadOnlySet<string> DirectoryDependencies;

        /// <summary>
        /// Output directories.
        /// </summary>
        public readonly IReadOnlySet<string> OutputDirectories;

        /// <summary>
        /// Executable.
        /// </summary>
        public readonly string? Executable;

        /// <summary>
        /// Arguments.
        /// </summary>
        public readonly string? Arguments;

        /// <summary>
        /// Working directory.
        /// </summary>
        public readonly string? WorkingDirectory;

        /// <summary>
        /// Environment variables.
        /// </summary>
        public readonly IReadOnlyDictionary<string, string> EnvVars;

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcessData"/>.
        /// </summary>
        public RemoteSandboxedProcessData(
            string? executable,
            string? arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string> envVars,
            IReadOnlySet<string> fileDependencies,
            IReadOnlySet<string> directoryDependencies,
            IReadOnlySet<string> outputDirectories,
            IReadOnlySet<string> tempDirectories,
            IReadOnlySet<string> untrackedScopes,
            IReadOnlySet<string> untrackedPaths)
        {
            Executable = executable;
            Arguments = arguments;
            WorkingDirectory = workingDirectory;
            EnvVars = envVars;
            FileDependencies = fileDependencies;
            DirectoryDependencies = directoryDependencies;
            OutputDirectories = outputDirectories;
            TempDirectories = tempDirectories;
            UntrackedScopes = untrackedScopes;
            UntrackedPaths = untrackedPaths;
        }

        /// <summary>
        /// Serializes this instance using an instance of <see cref="BuildXLWriter"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Executable);
            writer.Write(Arguments);
            writer.Write(WorkingDirectory);
            writer.WriteReadOnlyList(EnvVars.ToList(), (w, kvp) => { w.Write(kvp.Key); w.Write(kvp.Value); });
            writer.Write(FileDependencies, (w, s) => w.Write(s));
            writer.Write(DirectoryDependencies, (w, s) => w.Write(s));
            writer.Write(OutputDirectories, (w, s) => w.Write(s));
            writer.Write(TempDirectories, (w, s) => w.Write(s));
            writer.Write(UntrackedScopes, (w, s) => w.Write(s));
            writer.Write(UntrackedPaths, (w, s) => w.Write(s));
        }

        /// <summary>
        /// Serializes this instance to a stream.
        /// </summary>
        public void Serialize(Stream stream)
        {
            using var writer = new BuildXLWriter(false, stream, true, false);
            Serialize(writer);
        }

        /// <summary>
        /// Serialize this instance with tagging.
        /// </summary>
        public void TaggedSerialize(BuildXLWriter writer)
        {
            Tag.WriteTaggedString(writer, DataTag.Executable, Executable);
            Tag.WriteTaggedString(writer, DataTag.Arguments, Arguments);
            Tag.WriteTaggedString(writer, DataTag.WorkingDirectory, WorkingDirectory);
            Tag.WriteTaggedMap(writer, DataTag.EnvVars, EnvVars, Tag.WriteString, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.FileDependency, FileDependencies, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.DirectoryDependency, DirectoryDependencies, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.OutputDirectory, OutputDirectories, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.TempDirectory, TempDirectories, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.UntrackedScope, UntrackedScopes, Tag.WriteString);
            Tag.WriteRepeatedTaggedItems(writer, DataTag.UntrackedPath, UntrackedPaths, Tag.WriteString);

            // Must end with end marker.
            Tag.EndWrite(writer);
        }

        /// <summary>
        /// Serialize this instance with tagging to a stream.
        /// </summary>
        public void TaggedSerialize(Stream stream)
        {
            using var writer = new BuildXLWriter(false, stream, true, false);
            TaggedSerialize(writer);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="RemoteSandboxedProcessData"/> using an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static RemoteSandboxedProcessData Deserialize(BuildXLReader reader)
        {
            string executable = reader.ReadString();
            string arguments = reader.ReadString();
            string workingDirectory = reader.ReadString();
            IReadOnlyDictionary<string, string> envVars = reader
                .ReadReadOnlyList(r => new KeyValuePair<string, string>(r.ReadString(), r.ReadString()))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            IReadOnlySet<string> fileDependencies = reader.ReadReadOnlySet(r => r.ReadString());
            IReadOnlySet<string> directoryDependencies = reader.ReadReadOnlySet(r => r.ReadString());
            IReadOnlySet<string> outputDirectories = reader.ReadReadOnlySet(r => r.ReadString());
            IReadOnlySet<string> tempDirectories = reader.ReadReadOnlySet(r => r.ReadString());
            IReadOnlySet<string> untrackedScopes = reader.ReadReadOnlySet(r => r.ReadString());
            IReadOnlySet<string> untrackedPaths = reader.ReadReadOnlySet(r => r.ReadString());

            return new RemoteSandboxedProcessData(
                executable,
                arguments,
                workingDirectory,
                envVars,
                fileDependencies,
                directoryDependencies,
                outputDirectories,
                tempDirectories,
                untrackedScopes,
                untrackedPaths);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="RemoteSandboxedProcessData"/> from a stream.
        /// </summary>
        public static RemoteSandboxedProcessData Deserialize(Stream stream)
        {
            using var reader = new BuildXLReader(false, stream, true);
            return Deserialize(reader);
        }

        /// <summary>
        /// Deserializes a tagged instance of <see cref="RemoteSandboxedProcessData"/> using an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static RemoteSandboxedProcessData TaggedDeserialize(BuildXLReader reader)
        {
            byte tag;
            string? executable = null;
            string? arguments = null;
            string? workingDirectory = null;
            var envVars = new Dictionary<string, string>();
            var fileDependencies = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var directoryDependencies = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var outputDirectories = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var tempDirectories = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var untrackedScopes = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);
            var untrackedPaths = new ReadOnlyHashSet<string>(OperatingSystemHelper.PathComparer);

            string? s1 = null;
            string? s2 = null;
            int i1 = 0;
            int i2 = 0;

            while (!Tag.ReachedEnd(tag = Tag.ReadTag(reader)))
            {
                ReadTypedValue(reader, ref s1, ref i1, ref s2, ref i2);

                switch (tag)
                {
                    case DataTag.Executable:
                        executable = s1;
                        break;
                    case DataTag.Arguments:
                        arguments = s1;
                        break;
                    case DataTag.WorkingDirectory:
                        workingDirectory = s1;
                        break;
                    case DataTag.EnvVars:
                        envVars[s1!] = s2!;
                        break;
                    case DataTag.FileDependency:
                        fileDependencies.Add(s1!);
                        break;
                    case DataTag.DirectoryDependency:
                        directoryDependencies.Add(s1!);
                        break;
                    case DataTag.OutputDirectory:
                        outputDirectories.Add(s1!);
                        break;
                    case DataTag.TempDirectory:
                        tempDirectories.Add(s1!);
                        break;
                    case DataTag.UntrackedScope:
                        untrackedScopes.Add(s1!);
                        break;
                    case DataTag.UntrackedPath:
                        untrackedPaths.Add(s1!);
                        break;
                    default:
                        // Ignore unrecognized tag.
                        break;
                }
            }

            return new RemoteSandboxedProcessData(
                executable,
                arguments,
                workingDirectory,
                envVars,
                fileDependencies,
                directoryDependencies,
                outputDirectories,
                tempDirectories,
                untrackedScopes,
                untrackedPaths);
        }

        private static void ReadTypedValue(BuildXLReader reader, ref string? mainStr, ref int mainNum, ref string? altStr, ref int altNum)
        {
            byte type = Tag.ReadType(reader);
            switch (type)
            {
                case TagType.String:
                    mainStr = Tag.ReadString(reader);
                    break;
                case TagType.Number:
                    mainNum = Tag.ReadNumber(reader);
                    break;
                case TagType.Map:
                    ReadTypedValue(reader, ref mainStr, ref mainNum, ref altStr, ref altNum);
                    ReadTypedValue(reader, ref altStr, ref altNum, ref mainStr, ref mainNum);
                    break;
            }
        }


        /// <summary>
        /// Deserializes a tagged instance of <see cref="RemoteSandboxedProcessData"/> from a stream.
        /// </summary>
        public static RemoteSandboxedProcessData TaggedDeserialize(Stream stream)
        {
            using var reader = new BuildXLReader(false, stream, true);
            return TaggedDeserialize(reader);
        }

        /// <summary>
        /// Checks if a file access path is untracked.
        /// </summary>
        public bool IsUntracked(string fileAccessPath)
        {
            string? normalizedPath = NormalizedInputPathOrNull(fileAccessPath);

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

        private static string? NormalizedInputPathOrNull(string path)
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
            private readonly HashSet<AbsolutePath> m_fileDependencies = new ();
            private readonly HashSet<AbsolutePath> m_directoryDependencies = new ();
            private readonly HashSet<AbsolutePath> m_outputDirectories = new ();
            private readonly HashSet<AbsolutePath> m_untrackedScopes = new ();
            private readonly HashSet<AbsolutePath> m_untrackedPaths = new ();
            private readonly HashSet<AbsolutePath> m_tempDirectories = new ();
            private SandboxedProcessInfo? m_processInfo;
            private readonly IRemoteProcessManager m_remoteProcessManager;
            private readonly Process m_process;
            private readonly PathTable m_pathTable;

            /// <summary>
            /// Constructor.
            /// </summary>
            public Builder(IRemoteProcessManager remoteProcessManager, Process process, PathTable pathTable)
            {
                m_remoteProcessManager = remoteProcessManager;
                m_process = process;
                m_pathTable = pathTable;
            }

            /// <summary>
            /// Adds an untracked scope.
            /// </summary>
            public void AddUntrackedScope(AbsolutePath path) => m_untrackedScopes.Add(path);

            /// <summary>
            /// Adds an untracked path.
            /// </summary>
            public void AddUntrackedPath(AbsolutePath path) => m_untrackedPaths.Add(path);

            /// <summary>
            /// Adds a temp directory.
            /// </summary>
            public void AddTempDirectory(AbsolutePath path) => m_tempDirectories.Add(path);

            /// <summary>
            /// Adds a file dependency.
            /// </summary>
            public void AddFileDependency(AbsolutePath path) => m_fileDependencies.Add(path);

            /// <summary>
            /// Adds a directory dependency.
            /// </summary>
            public void AddDirectoryDependency(AbsolutePath path) => m_directoryDependencies.Add(path);

            /// <summary>
            /// Adds an output directory.
            /// </summary>
            /// <param name="path"></param>
            public void AddOutputDirectory(AbsolutePath path) => m_outputDirectories.Add(path);

            /// <summary>
            /// Sets process info.
            /// </summary>
            public void SetProcessInfo(SandboxedProcessInfo processInfo) => m_processInfo = processInfo;

            /// <summary>
            /// Builds an instance of <see cref="RemoteSandboxedProcessData"/>.
            /// </summary>
            public async Task<RemoteSandboxedProcessData> BuildAsync()
            {
                Contract.Requires(m_processInfo != null);

                m_fileDependencies.UnionWith(await m_remoteProcessManager.GetInputPredictionAsync(m_process));

                return new RemoteSandboxedProcessData(
                    m_processInfo!.FileName,
                    m_processInfo!.Arguments,
                    m_processInfo!.WorkingDirectory,
                    m_processInfo!.EnvironmentVariables.ToDictionary(),
                    ToStringPathSet(m_fileDependencies),
                    ToStringPathSet(m_directoryDependencies),
                    ToStringPathSet(m_outputDirectories),
                    ToStringPathSet(m_tempDirectories),
                    ToStringPathSet(m_untrackedScopes),
                    ToStringPathSet(m_untrackedPaths));
            }

            private IReadOnlySet<string> ToStringPathSet(HashSet<AbsolutePath> paths) =>
                new ReadOnlyHashSet<string>(paths.Select(p => p.ToString(m_pathTable)), OperatingSystemHelper.PathComparer);
        }
    }
}
