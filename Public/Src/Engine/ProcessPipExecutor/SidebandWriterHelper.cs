// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes.Sideband
{
    /// <summary>
    /// Helper function which includes all the functions that are Pip specific.
    /// </summary>
    /// <remarks>
    /// NOTE: not thread-safe
    /// 
    /// NOTE: must be serializable in order to be compatible with VM execution; for this 
    ///       reason, this class must not have a field of type <see cref="PathTable"/>.
    /// </remarks>
    public sealed class SidebandWriterHelper
    {
        private const string SidebandFilePrefix = "Pip";
        private const string SidebandFileSuffix = ".sideband";

        /// <summary>
        /// Creates a new output logger for a given process.
        /// 
        /// Shared opaque directory outputs of <paramref name="process"/> are used as root directories and
        /// <see cref="GetSidebandFileForProcess"/> is used as log base name.
        ///
        /// <seealso cref="SidebandWriter(SidebandMetadata, string, IReadOnlyList{string})"/>
        /// </summary>
        public static SidebandWriter CreateSidebandWriterFromProcess(SidebandMetadata metadata, PipExecutionContext context, Process process, AbsolutePath sidebandRootDirectory)
        {
            Contract.Requires(process != null);
            Contract.Requires(context != null);
            Contract.Requires(sidebandRootDirectory.IsValid);
            Contract.Requires(Directory.Exists(sidebandRootDirectory.ToString(context.PathTable)));

            return new SidebandWriter(
                metadata,
                GetSidebandFileForProcess(context.PathTable, sidebandRootDirectory, process),
                process.DirectoryOutputs.Where(d => d.IsSharedOpaque).Select(d => d.Path.ToString(context.PathTable)).ToList());
        }

        /// <summary>
        /// Given a root directory (<paramref name="searchRootDirectory"/>), returns the full path to the sideband file corresponding to process <paramref name="process"/>.
        /// </summary>
        public static string GetSidebandFileForProcess(PathTable pathTable, AbsolutePath searchRootDirectory, Process process)
        {
            Contract.Requires(searchRootDirectory.IsValid);

            var semiStableHashX16 = string.Format(CultureInfo.InvariantCulture, "{0:X16}", process.SemiStableHash);
            var subDirName = semiStableHashX16.Substring(0, 3);

            return searchRootDirectory.Combine(pathTable, subDirName).Combine(pathTable, $"{SidebandFilePrefix}{semiStableHashX16}{SidebandFileSuffix}").ToString(pathTable);
        }

        /// <summary>
        /// Finds and returns all sideband files that exist in directory denoted by <paramref name="directory"/>
        /// </summary>
        /// <remarks>
        /// CODESYNC: must be consistent with <see cref="GetSidebandFileForProcess(PathTable, AbsolutePath, Process)"/>
        /// </remarks>
        public static string[] FindAllProcessPipSidebandFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, $"{SidebandFilePrefix}*{SidebandFileSuffix}", SearchOption.AllDirectories).ToArray()
                : CollectionUtilities.EmptyArray<string>();
        }

        /// <summary>
        /// Returns all paths recorded in the <paramref name="filePath"/> file, even if the
        /// file appears to be corrupted.
        /// 
        /// If file at <paramref name="filePath"/> does not exist, returns an empty iterator.
        /// 
        /// <seealso cref="SidebandReader.ReadRecordedPaths"/>
        /// </summary>
        public static string[] ReadRecordedPathsFromSidebandFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return CollectionUtilities.EmptyArray<string>();
            }

            using (var reader = new SidebandReader(filePath))
            {
                reader.ReadHeader(ignoreChecksum: true);
                reader.ReadMetadata();
                return reader.ReadRecordedPaths().ToArray();
            }
        }
    }
}
