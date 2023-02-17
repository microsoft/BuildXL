// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for the custom Yarn resolver
    /// </summary>
    public interface ICustomJavaScriptResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The project-to-project graph of the repo
        /// </summary>
        /// <remarks>
        /// User can provide either a path to a project file that is expected to follow the Yarn workspaces schema (https://classic.yarnpkg.com/en/docs/cli/workspaces/#toc-yarn-workspaces-info)
        /// or an equivalent object literal
        /// </remarks>
        DiscriminatingUnion<AbsolutePath, IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode>> CustomProjectGraph { get; }
    }
}
