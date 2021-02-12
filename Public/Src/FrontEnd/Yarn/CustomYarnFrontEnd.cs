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
    /// Resolver frontend that can schedule customized Yarn projects.
    /// </summary>
    public sealed class CustomYarnFrontEnd : FrontEnd<CustomYarnWorkspaceResolver>
    {
        /// <inheritdoc />
        public override string Name => KnownResolverKind.CustomJavaScriptResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.CustomJavaScriptResolverKind };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new JavaScriptResolver<YarnConfiguration, ICustomJavaScriptResolverSettings>(Host, Context, Name);
        }
    }
}
