// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// The result of inspecting a package with <see cref="NugetPackageInspector"/>
    /// </summary>
    public readonly struct NugetInspectedPackage
    {
        /// <nodoc/>
        public readonly string Nuspec { get; }

        /// <nodoc/>
        public readonly IReadOnlyList<RelativePath> Content { get; }

        /// <nodoc/>
        public NugetInspectedPackage(string nuspec, IReadOnlyList<RelativePath> content)
        {
            Contract.RequiresNotNull(nuspec);
            Contract.RequiresNotNull(content);

            Nuspec = nuspec;
            Content = content;
        }
    }
}
