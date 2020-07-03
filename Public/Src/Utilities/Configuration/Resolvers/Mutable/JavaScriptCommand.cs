// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptCommand : IJavaScriptCommand
    {
        /// <nodoc />
        public JavaScriptCommand()
        {
            Command = string.Empty;
            DependsOn = new List<IJavaScriptCommandDependency>();
        }

        /// <nodoc />
        public JavaScriptCommand(IJavaScriptCommand template)
        {
            Command = template.Command;
            DependsOn = template.DependsOn ?? new List<IJavaScriptCommandDependency>();
        }

        /// <inheritdoc/>
        public string Command { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<IJavaScriptCommandDependency> DependsOn { get; set; }
    }
}
