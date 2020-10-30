// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptCommandGroupWithDependencies : JavaScriptCommandGroup, IJavaScriptCommandGroupWithDependencies
    {
        /// <nodoc />
        public JavaScriptCommandGroupWithDependencies()
        {
            CommandName = string.Empty;
            Commands = new List<string>();
            DependsOn = new List<IJavaScriptCommandDependency>();
        }

        /// <nodoc />
        public JavaScriptCommandGroupWithDependencies(IJavaScriptCommandGroupWithDependencies template)
        {
            CommandName = template.CommandName;
            Commands = template.Commands;
            DependsOn = template.DependsOn ?? new List<IJavaScriptCommandDependency>();
        }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptCommandDependency> DependsOn { get; set; }
    }
}
