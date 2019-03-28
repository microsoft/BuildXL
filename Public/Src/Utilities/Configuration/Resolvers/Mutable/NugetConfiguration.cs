// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class NugetConfiguration : ArtifactLocation, INugetConfiguration
    {
        /// <nodoc />
        public NugetConfiguration()
        {
            CredentialProviders = new List<IArtifactLocation>();
        }

        /// <nodoc />
        public NugetConfiguration(INugetConfiguration template)
            : base(template)
        {
            CredentialProviders = new List<IArtifactLocation>(template.CredentialProviders);
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<IArtifactLocation> CredentialProviders { get; set; }

        /// <inheritdoc />
        IReadOnlyList<IArtifactLocation> INugetConfiguration.CredentialProviders => CredentialProviders;
    }
}
