// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine;
using BuildXL.Pips.Operations;

namespace Tool.ExecutionLogSdk
{
    public interface IPipFilter
    {
        /// <summary>
        /// Returns true to indicate that the process pip should be loaded
        /// </summary>
        /// <param name="process">The process pip to load or not</param>
        /// <param name="cachedGraph">The build graph data that can be useful in deciding whether to load the pip</param>
        /// <returns>True if the process pip supplied should be loaded</returns>
        bool ShouldLoadPip(Process process, CachedGraph cachedGraph);
    }
}
