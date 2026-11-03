// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Defines the information needed for caching of a pip.
    /// NOTE: No behavior should be defined in this class
    /// </summary>
    public class CacheablePipInfo : PipInfo
    {
        /// <nodoc />
        public CacheablePipInfo(
            Pip pip,
            PipExecutionContext context,
            ReadOnlyArray<FileArtifactWithAttributes> outputs,
            ReadOnlyArray<FileArtifact> dependencies,
            ReadOnlyArray<DirectoryArtifact> directoryOutputs,
            ReadOnlyArray<DirectoryArtifact> directoryDependencies,
            ReadOnlyArray<FileArtifact> hashOnlyFileDependencies,
            ReadOnlyArray<DirectoryArtifact> hashOnlyDirectoryDependencies)
            : base(pip, context)
        {
            CacheableStaticOutputsCount = ProcessExtensions.GetCacheableOutputsCount(outputs);
            Outputs = outputs;
            RegularDependencies = dependencies;
            DirectoryOutputs = directoryOutputs;
            RegularDirectoryDependencies = directoryDependencies;
            HashOnlyFileDependencies = hashOnlyFileDependencies;
            HashOnlyDirectoryDependencies = hashOnlyDirectoryDependencies;
        }

        /// <summary>
        /// Gets number of items in the outputs that should be presented in cache.
        /// </summary>
        public int CacheableStaticOutputsCount { get; private set; }

        /// <summary>
        /// File outputs. Each member of the array is distinct.
        /// </summary>
        /// <remarks>
        /// <code>Dependencies</code> and <code>Outputs</code>
        /// together must mention all file artifacts referenced by other properties of this Pip.
        /// Every output artifact contains an <see cref="FileExistence"/> attribute associated with it.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<FileArtifactWithAttributes> Outputs { get; private set; }

        /// <summary>
        /// Regular file inputs requiring filesystem access. Each member of the array is distinct.
        /// </summary>
        /// <remarks>
        /// <code>Dependencies</code> and
        /// <code>Outputs</code>
        /// together must mention all file artifacts referenced by other properties of this Pip.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<FileArtifact> RegularDependencies { get; private set; }

        /// <summary>
        /// Directory outputs. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<DirectoryArtifact> DirectoryOutputs { get; private set; }

        /// <summary>
        /// Regular directory inputs requiring filesystem access. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<DirectoryArtifact> RegularDirectoryDependencies { get; private set; }

        /// <summary>
        /// File content dependencies that do not grant filesystem access.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<FileArtifact> HashOnlyFileDependencies { get; }

        /// <summary>
        /// Directory content dependencies that do not grant filesystem access.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<DirectoryArtifact> HashOnlyDirectoryDependencies { get; }

        /// <summary>
        /// Enumerates all file content dependencies without copying the underlying arrays.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public IEnumerable<FileArtifact> Dependencies => HashOnlyFileDependencies.Length == 0
            ? RegularDependencies
            : RegularDependencies.Concat(HashOnlyFileDependencies);

        /// <summary>
        /// Enumerates all directory content dependencies without copying the underlying arrays.
        /// </summary>
        [PipCaching(FingerprintingRole = FingerprintingRole.None)]
        public IEnumerable<DirectoryArtifact> DirectoryDependencies => HashOnlyDirectoryDependencies.Length == 0
            ? RegularDirectoryDependencies
            : RegularDirectoryDependencies.Concat(HashOnlyDirectoryDependencies);

        /// <summary>
        /// Creates a cacheable pip info for an ipc pip
        /// </summary>
        public static CacheablePipInfo GetIpcCacheInfo(IpcPip pip, PipExecutionContext context, bool omitLazilyMaterializedDependencies)
        {
            var fileDependencies = omitLazilyMaterializedDependencies && pip.LazilyMaterializedDependencies.Any(a => a.IsFile)
                ? ReadOnlyArray<FileArtifact>.From(
                    pip.FileDependencies.Except(
                        pip.LazilyMaterializedDependencies.Where(a => a.IsFile).Select(a => a.FileArtifact)))
                : pip.FileDependencies;
            
            var directoryDependencies = omitLazilyMaterializedDependencies && pip.LazilyMaterializedDependencies.Any(a => a.IsDirectory)
                ? ReadOnlyArray<DirectoryArtifact>.From(
                    pip.DirectoryDependencies.Except(
                        pip.LazilyMaterializedDependencies.Where(a => a.IsDirectory).Select(a => a.DirectoryArtifact)))
                : pip.DirectoryDependencies;

            return new CacheablePipInfo(
                pip: pip,
                context: context,
                outputs: ReadOnlyArray<FileArtifactWithAttributes>.FromWithoutCopy(pip.OutputFile.WithAttributes()),
                dependencies: fileDependencies,
                directoryOutputs: ReadOnlyArray<DirectoryArtifact>.Empty,
                directoryDependencies: directoryDependencies,
                hashOnlyFileDependencies: ReadOnlyArray<FileArtifact>.Empty,
                hashOnlyDirectoryDependencies: ReadOnlyArray<DirectoryArtifact>.Empty);
        }
    }
}
