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

    /// <summary>
    /// Helper methods for <see cref="AsyncOut{T}"/>
    /// </summary>
    public static class AsyncOut
    {
        /// <summary>
        /// Allows inline declaration of <see cref="AsyncOut{T}"/> patterns like the
        /// (out T parameter) pattern. Usage: await ExecuteAsync(out AsyncOut.Var&lt;T&gt;(out var outParam));
        /// </summary>
        public static AsyncOut<T> Var<T>(out AsyncOut<T> value)
        {
            value = new AsyncOut<T>();
            return value;
        }
    }
}
