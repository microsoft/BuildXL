// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Resolver frontend that can schedule Lage projects.
    /// </summary>
    public sealed class LageFrontEnd : FrontEnd<LageWorkspaceResolver>
    {
        /// <inheritdoc />
        public override string Name => KnownResolverKind.LageResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.LageResolverKind };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new LageResolver(Host, Context, Name);
        }
    }
}
