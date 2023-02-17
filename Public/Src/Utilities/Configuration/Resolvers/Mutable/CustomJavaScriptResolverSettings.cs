// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the custom JavaScript front-end.
    /// </summary>
    public class CustomJavaScriptResolverSettings : JavaScriptResolverSettings, ICustomJavaScriptResolverSettings
    { 
        /// <nodoc/>
        public CustomJavaScriptResolverSettings()
        {
        }

        /// <nodoc/>
        public CustomJavaScriptResolverSettings(ICustomJavaScriptResolverSettings resolverSettings, PathRemapper pathRemapper) : base(resolverSettings, pathRemapper)
        {
            if (resolverSettings.CustomProjectGraph?.GetValue() is AbsolutePath absolutePath)
            {
                CustomProjectGraph = new DiscriminatingUnion<AbsolutePath, IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode>>(pathRemapper.Remap(absolutePath));
            }
            else
            {
                CustomProjectGraph = resolverSettings.CustomProjectGraph;
            }
        }

        /// <inheritdoc/>
        public DiscriminatingUnion<AbsolutePath, IReadOnlyDictionary<string, IJavaScriptCustomProjectGraphNode>> CustomProjectGraph { get; set; }
    }
}
