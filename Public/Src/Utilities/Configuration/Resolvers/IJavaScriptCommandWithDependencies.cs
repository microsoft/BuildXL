// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    ///  A JavaScript command with explicit dependencies
    /// </summary>
    public interface IJavaScriptCommandWithDependencies
    {
        /// <nodoc/>
        IReadOnlyList<IJavaScriptCommandDependency> DependsOn {get;}
    }
}
