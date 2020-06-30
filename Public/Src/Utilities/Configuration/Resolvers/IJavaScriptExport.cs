// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// An exported value to other resolvers. 
    /// </summary>
    /// <remarks>
    /// A symbol name must be specified (for now, no namespaces are allowed, just a plain name, e.g. 'outputs').
    /// The resolver will expose 'symbolName : StaticDirectory[]' value, with all the output directories from the projects 
    /// specified as content.
    /// A project can be just a package name(that will be matched against names declared in package.json), in which case the exposed
    /// outputs under a given symbol will be of all the commands in that project, or it can be a <see cref="IJavaScriptProjectOutputs"/>, 
    /// where specific script commands can be specified.
    /// </remarks>
    public interface IJavaScriptExport
    {
        /// <nodoc/>
        FullSymbol SymbolName { get; }

        /// <nodoc/>
        IReadOnlyList<DiscriminatingUnion<string, IJavaScriptProjectOutputs>> Content { get; }
    }
}
