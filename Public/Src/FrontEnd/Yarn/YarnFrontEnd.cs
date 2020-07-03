// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Yarn.ProjectGraph;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Yarn
{
    /// <summary>
    /// Resolver frontend that can schedule Yarn projects.
    /// </summary>
    public sealed class YarnFrontEnd : FrontEnd<YarnWorkspaceResolver>
    {
        /// <inheritdoc />
        public override string Name => KnownResolverKind.YarnResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.YarnResolverKind };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new JavaScriptResolver<YarnConfiguration, IYarnResolverSettings>(Host, Context, Name);
        }
    }
}
