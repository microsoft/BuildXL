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
            SymbolName = FullSymbol.Invalid;
            Content = new List<DiscriminatingUnion<string, IJavaScriptProjectOutputs>>();
        }

        /// <nodoc />
        public JavaScriptExport(IJavaScriptExport template)
        {
            SymbolName = template.SymbolName;
            Content = template.Content ?? new List<DiscriminatingUnion<string, IJavaScriptProjectOutputs>>();
        }

        /// <inheritdoc/>
        public FullSymbol SymbolName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectOutputs>> Content { get; set; }
    }
}
