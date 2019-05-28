// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
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
            ReadOnlyArray<DirectoryArtifact> directoryDependencies)
            : base(pip, context)
        {
            CacheableStaticOutputsCount = ProcessExtensions.GetCacheableOutputsCount(outputs);
            Outputs = outputs;
            Dependencies = dependencies;
            DirectoryOutputs = directoryOutputs;
            DirectoryDependencies = directoryDependencies;
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
        /// File dependencies. Each member of the array is distinct.
        /// </summary>
        /// <remarks>
        /// <code>Dependencies</code> and
        /// <code>Outputs</code>
        /// together must mention all file artifacts referenced by other properties of this Pip.
        /// </remarks>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<FileArtifact> Dependencies { get; private set; }

        /// <summary>
        /// Directory outputs. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Semantic)]
        public ReadOnlyArray<DirectoryArtifact> DirectoryOutputs { get; private set; }

        /// <summary>
        /// Directory dependencies. Each member of the array is distinct.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        [PipCaching(FingerprintingRole = FingerprintingRole.Content)]
        public ReadOnlyArray<DirectoryArtifact> DirectoryDependencies { get; private set; }

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
                directoryDependencies: directoryDependencies);
        }
    }
}
