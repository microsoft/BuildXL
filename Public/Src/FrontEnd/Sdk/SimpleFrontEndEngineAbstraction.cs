// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;
using IFileSystem = BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Simple implementation of FrontEndEngineAbstraction that implements:
    ///   1. operations for accessing files on disk (e.g., <see cref="TryGetFrontEndFile"/>, <see cref="GetFileContentAsync"/>),
    ///   2. operations related to environment variables (e.g., <see cref="TryGetBuildParameter"/>), and
    ///   3. mount points defined in the config.
    ///
    /// All operations related to tracking files/directories (e.g., <see cref="FinishTrackingBuildParameters"/>,
    /// <see cref="TrackDirectory(string)"/>) are no-ops.
    /// </summary>
    public class SimpleFrontEndEngineAbstraction : FrontEndEngineAbstraction
    {
        private HashSet<string> m_allowedBuildParameters;

        private readonly Dictionary<string, string> m_environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly IFileSystem m_fileSystem;

        /// <nodoc/>
        protected readonly PathTable m_pathTable;


        /// <summary>
        /// Mount names defined in the configuration (populated during construction).
        /// </summary>
        [CanBeNull]
        protected readonly Dictionary<string, IMount> m_customMountsTable;

        /// <nodoc />
        public SimpleFrontEndEngineAbstraction(PathTable pathTable, IFileSystem filesystem, IConfiguration configuration = null)
        {
            m_pathTable = pathTable;
            m_fileSystem = filesystem;

            m_environmentVariables.Add("BUILDXL_IS_ELEVATED", CurrentProcess.IsElevated.ToString());
            m_customMountsTable = ConstructMountsTable(configuration, pathTable.StringTable);

            var testRoot = AbsolutePath.Create(pathTable, Path.GetDirectoryName(AssemblyHelper.GetAssemblyLocation(GetType().Assembly)));

            Layout = new LayoutConfiguration()
            {
                ObjectDirectory = testRoot.Combine(pathTable, "obj_test"),
                RedirectedDirectory = testRoot.Combine(pathTable, "redirected_test"),
                FrontEndDirectory = testRoot.Combine(pathTable, "frontend"),
                TempDirectory = testRoot.Combine(pathTable, "tmp_test"),
            };
        }

        /// <summary>
        /// This method is not implemented.
        /// </summary>
        public override Task<Possible<ContentMaterializationResult, Failure>> TryMaterializeContentAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            ContentHash contentHash,
            bool trackPath = true,
            bool recordPathInFileContentTable = true) => throw new NotImplementedException();

        /// <inheritdoc />
        public override bool TryGetFrontEndFile(AbsolutePath path, string frontEnd, out Stream stream)
        {
            stream = null;
            var physicalPath = path.ToString(m_pathTable);
            if (!m_fileSystem.Exists(AbsolutePath.Create(m_pathTable, physicalPath)))
            {
                return false;
            }

            stream =  m_fileSystem.OpenText(AbsolutePath.Create(m_pathTable, physicalPath)).BaseStream;

            return true;
        }

        /// <inheritdoc />
        public override async Task<Possible<FileContent, RecoverableExceptionFailure>> GetFileContentAsync(AbsolutePath path)
        {
            Stream stream;
            if (TryGetFrontEndFile(path, "dummyFrontEnd", out stream))
            {
                var result = await FileContent.ReadFromAsync(stream);
#pragma warning disable AsyncFixer02
                stream?.Dispose();
#pragma warning restore AsyncFixer02

                return result;
            }

            string message = I($"File '{path.ToString(m_pathTable)}' is not found");
            return new Possible<FileContent, RecoverableExceptionFailure>(new RecoverableExceptionFailure(new BuildXLException(message, new FileNotFoundException(message))));
        }

        /// <inheritdoc />
        public override bool FileExists(AbsolutePath path)
        {
            var physicalPath = path.ToString(m_pathTable);
            return m_fileSystem.Exists(AbsolutePath.Create(m_pathTable, physicalPath));
        }

        /// <inheritdoc />
        public override bool DirectoryExists(AbsolutePath path)
        {
            var physicalPath = path.ToString(m_pathTable);
            return m_fileSystem.IsDirectory(path);
        }

        /// <inheritdoc />
        public override void RecordFrontEndFile(AbsolutePath path, string frontEnd)
        {
        }

        /// <inheritdoc />
        public override bool TryGetBuildParameter(string name, string frontEnd, out string value)
        {
            if (frontEnd == "DScript")
            {
                value = Environment.GetEnvironmentVariable(name);
                return true;
            }

            if (m_allowedBuildParameters != null)
            {
                if (!m_allowedBuildParameters.Contains(name))
                {
                    value = null;
                    return false;
                }

                m_environmentVariables.TryGetValue(name, out value);
                value = value ?? string.Empty;
                return true;
            }

            return m_environmentVariables.TryGetValue(name, out value);
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetMountNames(string frontEnd, ModuleId moduleId)
        {
            return m_customMountsTable != null
                ? m_customMountsTable.Keys
                : Enumerable.Empty<string>();
        }

        /// <inheritdoc />
        public override TryGetMountResult TryGetMount(string name, string frontEnd, ModuleId moduleId, out IMount mount)
        {
            mount = null;

            if (string.IsNullOrEmpty(name))
            {
                return TryGetMountResult.NameNullOrEmpty;
            }

            if (m_customMountsTable.TryGetValue(name, out mount) == true)
            {
                return TryGetMountResult.Success;
            }

            return TryGetMountResult.NameNotFound;
        }

        /// <inheritdoc />
        public override void RestrictBuildParameters(IEnumerable<string> buildParameterNames)
        {
        }

        /// <nodoc />
        public void AddBuildParameter(string name, string value)
        {
            m_environmentVariables.Add(name, value);
        }

        /// <nodoc />
        public void AllowBuildParameter(string name)
        {
            m_allowedBuildParameters = m_allowedBuildParameters ?? new HashSet<string>();
            m_allowedBuildParameters.Add(name);
        }

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath, IReadOnlyList<(string, FileAttributes)> members)
        {
        }

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath)
        {
        }

        /// <inheritdoc />
        public override void FinishTrackingBuildParameters()
        {
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetUnchangedFiles()
        {
            return null;
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetChangedFiles()
        {
            return null;
        }

        /// <inheritdoc />
        public override async Task<ContentHash> GetFileContentHashAsync(string path, bool trackFile = true)
        {
            using (
                var fs = FileUtilities.CreateFileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Delete | FileShare.Read,
                    FileOptions.SequentialScan))
            {
                return await ContentHashingUtilities.HashContentStreamAsync(fs);
            }
        }

        /// <inheritdoc />
        public override bool IsEngineStatePartiallyReloaded()
        {
            return false;
        }

        /// <inheritdoc />
        public override IEnumerable<AbsolutePath> EnumerateEntries(AbsolutePath path, string pattern, bool recursive, bool directories)
        {
            return EnumerateEntriesHelper(m_pathTable, path, pattern, recursive, directories, m_fileSystem);
        }

        private static Dictionary<string, IMount> ConstructMountsTable([CanBeNull] IConfiguration configuration, StringTable stringTable)
        {
            if (configuration == null)
            {
                return null;
            }

            var result = new Dictionary<string, IMount>();

            // For BuildXL script tests it is much easier to fake mounts using custom mounts dictionary
            // instead of using real mounts table.
            // That's why this fake implementation has two different fake mount storages.
            foreach (IMount mount in configuration.Mounts ?? Enumerable.Empty<IMount>())
            {
                if (!mount.Name.IsValid)
                {
                    throw new InvalidOperationException("Mount has invalid name");
                }

                result.Add(mount.Name.ToString(stringTable), mount);
            }

            return result;
        }
    }
}
