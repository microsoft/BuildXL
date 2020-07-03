// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Interface that defines the data structure that holds the cache configuration data loaded from Json
    /// </summary>
    /// <remarks>
    /// For now we will use a Dictionary of string : object
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1710", Justification = "Temporary - it will not remain a full dictionary")]
    public interface ICacheConfigData : IDictionary<string, object>
    {
    }
}
