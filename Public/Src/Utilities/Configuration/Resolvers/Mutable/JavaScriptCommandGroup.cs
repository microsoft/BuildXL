// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptCommandGroup : IJavaScriptCommandGroup
    {
        /// <nodoc />
        public JavaScriptCommandGroup()
        {
            CommandName = string.Empty;
            Commands = new List<string>();
        }

        /// <nodoc />
        public JavaScriptCommandGroup(IJavaScriptCommandGroupWithDependencies template)
        {
            CommandName = template.CommandName;
            Commands = template.Commands;
        }

        /// <inheritdoc/>
        public string CommandName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> Commands { get; set; }
    }
}
