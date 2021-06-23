// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class JavaScriptExport : IJavaScriptExport
    {
        /// <nodoc />
        public JavaScriptExport()
        {
        }

        /// <nodoc />
        public JavaScriptExport(IJavaScriptExport template)
        {
            SymbolName = template.SymbolName;
            Content = template.Content;
        }

        /// <inheritdoc/>
        public FullSymbol SymbolName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Content { get; set; }
    }
}
