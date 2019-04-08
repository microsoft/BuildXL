// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// The downloaded nuget package
    /// </summary>
    public sealed class PackageOnDisk
    {
        /// <summary>
        /// The package details
        /// </summary>
        public INugetPackage Package { get; }

        /// <summary>
        /// The files in the package
        /// </summary>
        public PackageDownloadResult PackageDownloadResult { get; }

        /// <summary>
        /// The path to the embedded package config
        /// </summary>
        /// <remarks>
        /// A DScript module is signified by module.config.dsc.
        /// </remarks>
        public AbsolutePath ModuleConfigFile { get; }

        /// <summary>
        /// The path to the embedded NuSpec file
        /// </summary>
        public AbsolutePath NuSpecFile { get; }

        /// <nodoc />
        public AbsolutePath PackageFolder => PackageDownloadResult.TargetLocation;

        /// <nodoc />
        public IReadOnlyList<RelativePath> Contents => PackageDownloadResult.Contents;

        /// <nodoc />
        public PackageOnDisk(PathTable pathTable, INugetPackage package, PackageDownloadResult packageDownloadResult)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(package != null);
            Contract.Requires(packageDownloadResult != null);

            Package = package;
            PackageDownloadResult = packageDownloadResult;

            var nuspecFileName = PathAtom.Create(pathTable.StringTable, package.Id + ".nuspec");
            GetSpecialFiles(pathTable, nuspecFileName, packageDownloadResult, out var nuSpecFile, out var moduleConfigFile);

            NuSpecFile = nuSpecFile;
            ModuleConfigFile = moduleConfigFile;
        }

        private static void GetSpecialFiles(
            PathTable pathTable,
            PathAtom nuspecFileName,
            PackageDownloadResult packageDownloadResult,
            out AbsolutePath nuSpecFile,
            out AbsolutePath moduleConfigFile)
        {
            nuSpecFile = AbsolutePath.Invalid;
            moduleConfigFile = AbsolutePath.Invalid;

            foreach (var relativePath in packageDownloadResult.Contents)
            {
                // Check for special files.
                if (!relativePath.IsEmpty)
                {
                    var fileName = relativePath.GetName();
                    if (fileName.CaseInsensitiveEquals(pathTable.StringTable, nuspecFileName))
                    {
                        nuSpecFile = packageDownloadResult.TargetLocation.Combine(pathTable, relativePath);
                    }
                    else if (IsModuleConfigFileName(fileName, pathTable.StringTable))
                    {
                        moduleConfigFile = packageDownloadResult.TargetLocation.Combine(pathTable, relativePath);
                    }
                }
            }
        }

        private static bool IsModuleConfigFileName(PathAtom fileName, StringTable stringTable)
        {
            return ExtensionUtilities.IsModuleConfigurationFile(fileName.ToString(stringTable));
        }
    }
}
