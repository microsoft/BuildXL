// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class RushExport : IRushExport
    {
        /// <nodoc />
        public RushExport()
        {
            SymbolName = FullSymbol.Invalid;
            Content = new List<DiscriminatingUnion<string, IRushProjectOutputs>>();
        }

        /// <nodoc />
        public RushExport(IRushExport template)
        {
            SymbolName = template.SymbolName;
            Content = template.Content ?? new List<DiscriminatingUnion<string, IRushProjectOutputs>>();
        }

        /// <inheritdoc/>
        public FullSymbol SymbolName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IRushProjectOutputs>> Content { get; set; }
    }
}
