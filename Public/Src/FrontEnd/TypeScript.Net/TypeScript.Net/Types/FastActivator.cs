// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Custom activator that creates instances of a generic type without paying reflection cost.
    /// </summary>
    /// <typeparam name="T">Type to instanciate.</typeparam>
    public static class FastActivator<T> where T : new()
    {
        /// <summary>
        /// Extremely fast generic factory method that returns an instance
        /// of the type T.
        /// </summary>
        public static readonly Func<T> Create =
            DynamicModuleLambdaCompiler.GenerateFactory<T>();
    }
}
