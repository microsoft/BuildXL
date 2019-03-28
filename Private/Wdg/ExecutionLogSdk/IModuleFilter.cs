// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Engine;
using BuildXL.Pips.Operations;

namespace Tool.ExecutionLogSdk
{
    public interface IModuleFilter
    {
        /// <summary>
        /// Returns true to indicate that the specified module should be loaded
        /// </summary>
        /// <param name="module">The module to load or not</param>
        /// <param name="cachedGraph">The build graph data that can be useful in deciding whether to load the module</param>
        /// <returns>True if the supplied module should be loaded</returns>
        bool ShouldLoadModule(ModulePip module, CachedGraph cachedGraph);
    }
}
