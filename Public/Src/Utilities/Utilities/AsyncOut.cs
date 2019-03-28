// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Used to specify out paramater to async method.
    /// </summary>
    public sealed class AsyncOut<T>
    {
        /// <summary>
        /// The value
        /// </summary>
        public T Value;
    }
}
