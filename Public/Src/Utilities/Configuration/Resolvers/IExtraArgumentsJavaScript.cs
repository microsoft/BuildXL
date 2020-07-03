// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    ///  Appends arguments to an existing script defined in package.json
    /// </summary>
    public interface IExtraArgumentsJavaScript
    {
        /// <nodoc/>
        string Command { get; }

        /// <nodoc/>
        DiscriminatingUnion<JavaScriptArgument, IReadOnlyList<JavaScriptArgument>> ExtraArguments { get; }
    }
}
