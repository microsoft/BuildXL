// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class NugetPackage : INugetPackage
    {
        /// <nodoc />
        public NugetPackage()
        {
        }

        /// <nodoc />
        public NugetPackage(INugetPackage template)
        {
            Id = template.Id;
            Version = template.Version;
            Alias = template.Alias;
            Tfm = template.Tfm;
            DependentPackageIdsToSkip = template.DependentPackageIdsToSkip ?? new List<string>() { };
            DependentPackageIdsToIgnore = template.DependentPackageIdsToIgnore ?? new List<string>() { };
            ForceFullFrameworkQualifiersOnly = template.ForceFullFrameworkQualifiersOnly;
        }

        /// <inheritdoc />
        public string Id { get; set; }

        /// <inheritdoc />
        public string Version { get; set; }

        /// <inheritdoc />
        public string Alias { get; set; }

        /// <inheritdoc />
        public string Tfm { get; set; }

        /// <inheritdoc />
        public List<string> DependentPackageIdsToSkip { get; private set; }

        /// <inheritdoc />
        public List<string> DependentPackageIdsToIgnore { get; private set; }

        /// <inheritdoc />
        public bool ForceFullFrameworkQualifiersOnly { get; private set; } = false;
    }
}
