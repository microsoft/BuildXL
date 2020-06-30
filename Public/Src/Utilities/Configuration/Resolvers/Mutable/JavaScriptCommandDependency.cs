// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptCommandDependency : IJavaScriptCommandDependency
    {
        /// <summary>
        /// Keep in sync with DScript definition
        /// </summary>
        public const string Local = "local";

        /// <summary>
        /// Keep in sync with DScript definition
        /// </summary>
        public const string Package = "package";

        /// <nodoc />
        public JavaScriptCommandDependency()
        {
            Command = string.Empty;
            Kind = string.Empty;
        }

        /// <nodoc />
        public JavaScriptCommandDependency(IJavaScriptCommandDependency template)
        {
            Command = template.Command;
            Kind = template.Kind;
        }

        /// <inheritdoc/>
        public string Kind { get; set; }

        /// <inheritdoc/>
        public string Command { get; set; }

        /// <inheritdoc/>
        public bool IsLocalKind() => Kind == Local;
    }
}
