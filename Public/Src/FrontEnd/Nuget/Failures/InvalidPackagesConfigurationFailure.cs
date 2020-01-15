// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Configuration file has one or more NuGet packages with the same name or alias.
    /// </summary>
    public sealed class InvalidPackagesConfigurationFailure : NugetFailure
    {
        private readonly IReadOnlyList<string> m_packages;

        /// <nodoc />
        public InvalidPackagesConfigurationFailure(IReadOnlyList<string> packages)
            : base(FailureType.InvalidPackageConfiguration)
        {
            m_packages = packages;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            string packages = string.Join(", ", m_packages.Select(s => I($"'{s}'")));
            return I($"The following NuGet packages have invalid configuration in the config file: {packages}");
        }
    }
}
