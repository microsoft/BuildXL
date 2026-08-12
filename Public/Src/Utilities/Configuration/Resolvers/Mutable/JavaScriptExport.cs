// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

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
            IncludeProjectMapping = template.IncludeProjectMapping;
            AllowEmpty = template.AllowEmpty;
        }

        /// <inheritdoc/>
        public FullSymbol SymbolName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectSimpleSelector, IJavaScriptProjectRegexSelector>> Content { get; set; }

        /// <inheritdoc/>
        public bool? IncludeProjectMapping { get; set; }

        /// <inheritdoc/>
        public bool? AllowEmpty { get; set; }
    }
}
