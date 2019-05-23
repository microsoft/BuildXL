// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Sdk;
using IFileSystem = global::BuildXL.FrontEnd.Sdk.FileSystem.IFileSystem;

namespace BuildXL.FrontEnd.Script.Testing.Helper
{
    /// <summary>
    /// Test implementation of FrontEndEngineAbstraction so we can fake out most of the engine
    /// </summary>
    public sealed class TestEngineAbstraction : FrontEndEngineAbstraction
    {
        private Dictionary<string, string> m_buildParameters = new Dictionary<string, string>(StringComparer.Ordinal);

        private Dictionary<string, IMount> m_mounts = new Dictionary<string, IMount>(StringComparer.Ordinal);

        private PathTable m_pathTable;
        private StringTable m_stringTable;
        private IFileSystem m_fileSystem;

        /// <nodoc />
        public TestEngineAbstraction(PathTable pathTable, StringTable stringTable, AbsolutePath basePath, IFileSystem fileSystem)
        {
            m_pathTable = pathTable;
            m_stringTable = stringTable;
            m_fileSystem = fileSystem;
            Layout = new LayoutConfiguration()
            {
                ObjectDirectory = basePath.Combine(pathTable, "obj_test"),
                TempDirectory = basePath.Combine(pathTable, "tmp_test"),
            };
        }

        /// <inheritdoc />
        public override bool TryGetFrontEndFile(AbsolutePath path, string frontEnd, out Stream stream)
        {
            var physicalPath = path.ToString(m_pathTable);
            try
            {
                stream = FileUtilities.CreateFileStream(
                    physicalPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Delete | FileShare.Read,
                    FileOptions.SequentialScan);

                return true;
            }
            catch (BuildXLException)
            {
                stream = null;
                return false;
            }
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

            return FileContent.Invalid;
        }

        /// <inheritdoc />
        public override bool FileExists(AbsolutePath path)
        {
            return m_fileSystem.Exists(path);
        }

        /// <inheritdoc />
        public override bool DirectoryExists(AbsolutePath path)
        {

            return m_fileSystem.IsDirectory(path);
        }

        /// <inheritdoc />
        public override void RecordFrontEndFile(AbsolutePath path, string frontEnd)
        {
        }

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath)
        {
        }

        /// <inheritdoc />
        public override bool TryGetBuildParameter(string name, string frontEnd, out string value)
        {
            return m_buildParameters.TryGetValue(name, out value);
        }

        /// <nodoc />
        public void SetBuildParameter(string name, string value)
        {
            m_buildParameters[name] = value;
        }

        /// <nodoc />
        public void RemoveBuildParameter(string name)
        {
            if (m_buildParameters.ContainsKey(name))
            {
                m_buildParameters.Remove(name);
            }
        }

        /// <inheritdoc />
        public override IEnumerable<string> GetMountNames(string frontEnd, ModuleId moduleId)
        {
            return m_mounts.Keys;
        }

        /// <inheritdoc />
        public override TryGetMountResult TryGetMount(string name, string frontEnd, ModuleId moduleId, out IMount mount)
        {
            if (string.IsNullOrEmpty(name))
            {
                mount = null;
                return TryGetMountResult.NameNullOrEmpty;
            }

            if (m_mounts.TryGetValue(name, out mount))
            {
                return TryGetMountResult.Success;
            }

            mount = null;
            return TryGetMountResult.NameNotFound;
        }

        /// <nodoc />
        public void SetMountPoint(IMount mount)
        {
            m_mounts[mount.Name.ToString(m_stringTable)] = mount;
        }

        /// <nodoc />
        public void RemoveMountPoint(string name)
        {
            if (m_mounts.ContainsKey(name))
            {
                m_mounts.Remove(name);
            }
        }

        /// <inheritdoc />
        public override void RestrictBuildParameters(IEnumerable<string> buildParameterNames)
        {
        }

        /// <inheritdoc />
        public override void FinishTrackingBuildParameters()
        {
        }

        /// <inheritdoc />
        public override Task<Possible<ContentMaterializationResult, Failure>> TryMaterializeContentAsync(
            IArtifactContentCache cache,
            FileRealizationMode fileRealizationModes,
            AbsolutePath path,
            ContentHash contentHash,
            bool trackPath = true,
            bool recordPathInFileContentTable = true) => null;

        /// <inheritdoc />
        public override void TrackDirectory(string directoryPath, IReadOnlyList<(string, FileAttributes)> members)
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
        public override Task<ContentHash> GetFileContentHashAsync(string path, bool trackFile = true)
        {
            throw new NotImplementedException();
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
    }
}
