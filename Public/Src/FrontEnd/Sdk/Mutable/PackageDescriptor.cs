// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Sdk.Mutable
{
    /// <summary>
    /// Package descriptor.
    /// </summary>
    public sealed class PackageDescriptor : IPackageDescriptor
    {
        /// <summary>
        /// Package name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Human readable package name.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Package version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Publisher of package.
        /// </summary>
        public string Publisher { get; set; }

        /// <summary>
        /// Main file.
        /// </summary>
        public AbsolutePath Main { get; set; }

        /// <summary>
        /// The allowed qualifier values
        /// </summary>
        public IReadOnlyList<AbsolutePath> Projects { get; set; }

        /// <summary>
        /// The resolution semantics for this package.
        /// </summary>
        public NameResolutionSemantics? NameResolutionSemantics { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> AllowedDependencies { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> CyclicalFriendModules { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IMount> Mounts { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<AbsolutePath> ScrubDirectories { get; set; }
    }
}
