// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Extensions;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Nuget
{
    /// <nodoc />
    public class NugetFailure : Failure
    {
        /// <nodoc />
        public INugetPackage Package { get; }

        /// <nodoc />
        public readonly FailureType Type;

        /// <nodoc />
        public readonly Exception Exception;

        /// <nodoc />
        public NugetFailure(FailureType failureType, Exception e = null)
        {
            Type = failureType;
            Exception = e;
        }

        /// <nodoc />
        public NugetFailure(INugetPackage package, FailureType failureType, Exception e = null)
            : this(failureType, e)
        {
            Package = package;
        }

        /// <summary>
        /// Configuration has one or more nuget packages with the same name or with the same alias.
        /// </summary>
        public static NugetFailure DuplicatedPackagesWithTheSameIdOrAlias(IReadOnlyList<string> duplicates)
        {
            Contract.Requires(!duplicates.IsNullOrEmpty());

            return new DuplicatePackagesFailure(duplicates, DuplicateKind.IdOrAlias);
        }
        
        /// <summary>
        /// Configuration has one or more nuget packages with the same name id and version.
        /// </summary>
        public static NugetFailure DuplicatedPackagesWithTheSameIdAndVersion(IReadOnlyList<string> duplicates)
        {
            Contract.Requires(!duplicates.IsNullOrEmpty());

            return new DuplicatePackagesFailure(duplicates, DuplicateKind.IdPlusVersion);
        }

        /// <summary>
        /// Nuget.exe failed to restore a package.
        /// </summary>
        public static NugetFailure CreateNugetInvocationFailure(INugetPackage package, int exitCode, string stdOut, string stdErr)
        {
            Contract.Requires(package != null);
            Contract.Requires(exitCode != 0);

            // If the stdOut has the following text: 'NotFound http' or 'WARNING: Unable to find version', it means that the package name or version are not found.
            if (stdOut.Contains("NotFound http") || stdOut.Contains("WARNING: Unable to find version"))
            {
                return new CanNotFindPackageFailure(package);
            }

            return new NugetFailedWithNonZeroExitCodeFailure(package, exitCode, stdOut, stdErr);
        }

        /// <summary>
        /// Configuration has one or more nuget packages with an invalid configuration.
        /// </summary>
        public static NugetFailure InvalidConfiguration(IReadOnlyList<string> packages)
        {
            Contract.Requires(!packages.IsNullOrEmpty());

            return new InvalidPackagesConfigurationFailure(packages);
        }

        /// <inheritdoc />
        public override string Describe()
        {
            if (Package != null)
            {
                return I($"Failed to retrieve nuget package '{Package.Id}' version '{Package.Version}' due to {Type.ToString()}. {Exception?.ToStringDemystified()}");
            }

            return I($"Failed to process nuget packages due to {Type.ToString()}. {Exception?.ToStringDemystified()}");
        }

        /// <inheritdoc />
        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }

        /// <nodoc />
        public enum FailureType
        {
            /// <nodoc />
            FetchNugetExe,

            /// <nodoc />
            FetchCredentialProvider,

            /// <nodoc />
            WriteConfigFile,

            /// <nodoc />
            WriteSpecFile,

            /// <nodoc />
            CleanTargetFolder,

            /// <nodoc />
            NugetFailedWithNonZeroExitCode,

            /// <nodoc />
            NugetFailedWithIoException,

            /// <nodoc />
            ListPackageContents,

            /// <nodoc />
            ReadNuSpecFile,

            /// <nodoc />
            AnalyzeNuSpec,

            /// <nodoc />
            InvalidNuSpec,

            /// <nodoc />
            InvalidPackageConfiguration,

            /// <nodoc />
            CyclicPackageDependency,

            /// <nodoc />
            DuplicatePackageIdOnConfig,
            
            /// <nodoc />
            PackageNotFound,

            /// <nodoc />
            MissingMonoHome,

            /// <nodoc />
            UnhandledError,
        }
    }
}
