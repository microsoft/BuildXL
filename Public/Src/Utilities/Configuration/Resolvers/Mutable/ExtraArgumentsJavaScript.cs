// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class ExtraArgumentsJavaScript : IExtraArgumentsJavaScript
    {
        /// <nodoc />
        public ExtraArgumentsJavaScript()
        {}

        /// <nodoc />
        public ExtraArgumentsJavaScript(IExtraArgumentsJavaScript template)
        {
            Command = template.Command;
            ExtraArguments = template.ExtraArguments;
        }
        /// <inheritdoc/>
        public string Command { get; set; }

        /// <inheritdoc/>
        public DiscriminatingUnion<JavaScriptArgument, IReadOnlyList<JavaScriptArgument>> ExtraArguments { get; set; }
    }
}
