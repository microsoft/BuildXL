// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Package with a given name and version is not found.
    /// </summary>
    public sealed class CanNotFindPackageFailure : NugetFailure
    {
        private readonly INugetPackage m_package;

        /// <nodoc />
        public CanNotFindPackageFailure(INugetPackage package)
            : base(FailureType.PackageNotFound)
        {
            m_package = package;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            return I($"Package nuget://{m_package.Id}/{m_package.Version} could not be restored: the package is not found.");
        }
    }
}
