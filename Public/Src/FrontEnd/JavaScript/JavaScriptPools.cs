// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.JavaScript
{
    /// <summary>
    /// Object pools for JavaScript.
    /// </summary>
    public static class JavaScriptPools
    {
        /// <summary>
        /// Global pool of sets for collecting JavaScriptProject
        /// </summary>
        public static ObjectPool<HashSet<JavaScriptProject>> JavaScriptProjectSet { get; } = new ObjectPool<HashSet<JavaScriptProject>>(
            () => new HashSet<JavaScriptProject>(),
            s => s.Clear());

        /// <summary>
        /// Global pool of sets for collecting ILazyEval
        /// </summary>
        public static ObjectPool<HashSet<ILazyEval>> LazyEvalSet { get; } = new ObjectPool<HashSet<ILazyEval>>(
            () => new HashSet<ILazyEval>(),
            s => s.Clear());
    }
}
