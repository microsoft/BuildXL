// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Lage resolver
    /// </summary>
    public interface ILageResolverSettings : IJavaScriptResolverSettings
    {
        /// <nodoc/>
        IReadOnlyList<string> Targets {get;}
    }
}
