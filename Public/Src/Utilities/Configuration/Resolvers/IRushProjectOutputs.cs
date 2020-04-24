// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A Rush project with a name as specified in its corresponding package.json, together with a collection of script commmands
    /// to be included 
    /// </summary>
    public interface IRushProjectOutputs
    {
        /// <nodoc/>
        string PackageName { get; }

        /// <nodoc/>
        IReadOnlyList<string> Commands { get; }
    }
}
