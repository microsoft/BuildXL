// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
