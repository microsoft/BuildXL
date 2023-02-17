// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Data associated with a custom specified project name
    /// </summary>
    public interface IJavaScriptCustomProjectGraphNode
    {
        /// <nodoc/>
        public RelativePath Location { get; }

        /// <nodoc/>
        public IReadOnlyList<string> WorkspaceDependencies { get; }
    }
}