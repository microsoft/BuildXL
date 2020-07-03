// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Resolver frontend that can schedule Ninja projects
    /// </summary>
    public sealed class NinjaFrontEnd : FrontEnd<NinjaWorkspaceResolver>
    {
        /// <nodoc />
        public override string Name => NinjaWorkspaceResolver.NinjaResolverName;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters { get; } = false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { NinjaWorkspaceResolver.NinjaResolverName };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            Contract.Requires(kind == KnownResolverKind.NinjaResolverKind);

            return new NinjaResolver(Host, Context, Name);
        }
    }
}
