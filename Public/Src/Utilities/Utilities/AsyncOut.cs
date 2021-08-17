// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <nodoc />
        public static implicit operator T(AsyncOut<T> value)
        {
            return value.Value;
        }
    }
}
