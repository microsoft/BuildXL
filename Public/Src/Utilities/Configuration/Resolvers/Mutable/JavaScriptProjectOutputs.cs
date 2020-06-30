// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptProjectOutputs : IJavaScriptProjectOutputs
    {
        /// <nodoc />
        public JavaScriptProjectOutputs()
        {
            PackageName = string.Empty;
            Commands = new List<string>();
        }

        /// <nodoc />
        public JavaScriptProjectOutputs(IJavaScriptProjectOutputs template)
        {
            PackageName = template.PackageName;
            Commands = template.Commands ?? new List<string>();
        }

        /// <inheritdoc/>
        public string PackageName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Commands { get; set; }
    }
}
