// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.CMake
{
    /// <summary>
    /// Resolver frontend that can schedule CMake projects
    /// </summary>
    public sealed class CMakeFrontEnd : FrontEnd<CMakeWorkspaceResolver>
    {
        /// <inheritdoc />
        public override string Name => CMakeWorkspaceResolver.CMakeResolverName;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters { get; } = false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { CMakeWorkspaceResolver.CMakeResolverName };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            Contract.Requires(kind == KnownResolverKind.CMakeResolverKind);

            return new CMakeResolver(Host, Context, Name);
        }
    }
}
