// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
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
        public readonly string Message;

        /// <nodoc />
        public NugetFailure(FailureType failureType, Exception e = null)
        {
            Type = failureType;

            Message = GetAllMessages(e);
        }

        /// <nodoc />
        public NugetFailure(FailureType failureType, string message)
        {
            Type = failureType;
            Message = message;
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
        /// Nuget invocation failure
        /// </summary>
        public static NugetFailure CreateNugetInvocationFailure(INugetPackage package, Exception e)
        {
            return CreateNugetInvocationFailure(package, GetAllMessages(e));
        }

        /// <summary>
        /// Nuget invocation failure
        /// </summary>
        public static NugetFailure CreateNugetInvocationFailure(INugetPackage package, string message)
        {
            Contract.RequiresNotNull(package);
            Contract.RequiresNotNull(message);

            // If the stdOut has the following text: 'NotFound http' or 'WARNING: Unable to find version', it means that the package name or version are not found.
            if (message.Contains("NotFound http") || message.Contains("WARNING: Unable to find version"))
            {
                return new CanNotFindPackageFailure(package);
            }

            return new NugetInvocationFailure(package, message);
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
                return I($"Failed to retrieve nuget package '{Package.Id}' version '{Package.Version}' due to {Type.ToString()}. {Message}");
            }

            return I($"Failed to process nuget packages due to {Type.ToString()}. {Message}");
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
            FetchCredentialProvider,

            /// <nodoc />
            WriteSpecFile,

            /// <nodoc />
            CleanTargetFolder,

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

            /// <nodoc/>
            NoBaseAddressForRepository,
        }

        private static string GetAllMessages(Exception e)
        {
            if (e is null)
            {
                return string.Empty;
            }

            if (e is AggregateException aggregateException)
            {
                return string.Join(Environment.NewLine, aggregateException.Flatten().InnerExceptions.SelectMany(ie => GetInnerExceptions(ie)).Select(ex => ex.Message));
            }
            else
            {
                return string.Join(Environment.NewLine, GetInnerExceptions(e).Select(e => e.Message));
            }
        }

        private static IEnumerable<Exception> GetInnerExceptions(Exception ex)
        {
            var innerException = ex;
            do
            {
                yield return innerException;
                innerException = innerException.InnerException;
            }
            while (innerException != null);
        }
    }
}
