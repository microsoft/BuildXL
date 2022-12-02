// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Resolver frontend that can schedule MsBuild projects using the static graph API from MsBuild.
    /// </summary>
    public sealed class MsBuildFrontEnd : FrontEnd<MsBuildWorkspaceResolver>
    {
        /// <nodoc />
        public override string Name => MsBuildWorkspaceResolver.MsBuildResolverName;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters { get; } = false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { MsBuildWorkspaceResolver.MsBuildResolverName };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind) => new MsBuildResolver(Host, Context, Name);
    }
}
