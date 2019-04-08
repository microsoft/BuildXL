// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Nuget
{
    /// <nodoc />
    public enum DuplicateKind
    {
        /// <nodoc />
        IdOrAlias,

        /// <nodoc />
        IdPlusVersion,
    }

    /// <summary>
    /// Configuration file has one or more nuget package with the same name or alias.
    /// </summary>
    public sealed class DuplicatePackagesFailure : NugetFailure
    {
        private readonly IReadOnlyList<string> m_duplicatedPackages;
        private readonly DuplicateKind m_duplicateKind;

        /// <nodoc />
        public DuplicatePackagesFailure(IReadOnlyList<string> duplicatedPackages, DuplicateKind duplicateKind)
            : base(FailureType.DuplicatePackageIdOnConfig)
        {
            m_duplicatedPackages = duplicatedPackages;
            m_duplicateKind = duplicateKind;
        }

        /// <inheritdoc />
        public override string Describe()
        {
            string packages = string.Join(", ", m_duplicatedPackages.Select(s => I($"'{s}'")));
            return m_duplicateKind == DuplicateKind.IdOrAlias
             ? I($"The following nuget packages are specified multiple times in the config file: {packages}")
             : I($"The following nuget packages has the same id and version in the config file: {packages}");
        }
    }
}
