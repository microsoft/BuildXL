// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Resolver frontend that can schedule Rush projects.
    /// </summary>
    public sealed class RushFrontEnd : FrontEnd<RushWorkspaceResolver>
    {
        /// <inheritdoc />
        public override string Name => KnownResolverKind.RushResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.RushResolverKind };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new RushResolver(Host, Context, Name);
        }
    }
}
