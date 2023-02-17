// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptCustomProjectGraphNode : IJavaScriptCustomProjectGraphNode
    {
        /// <nodoc />
        public JavaScriptCustomProjectGraphNode()
        {
        }

        /// <nodoc />
        public JavaScriptCustomProjectGraphNode(IJavaScriptCustomProjectGraphNode template)
        {
            Location = template.Location;
            WorkspaceDependencies = template.WorkspaceDependencies;
        }

        /// <nodoc/>
        public RelativePath Location { get; set; }

        /// <nodoc/>
        public IReadOnlyList<string> WorkspaceDependencies { get; set; }
    }
}
