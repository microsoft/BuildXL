// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Boxed mutable reference to a value.
    /// </summary>
    public sealed class BoxRef<T>
    {
        /// <summary>
        /// The value
        /// </summary>
        public T Value;

        /// <summary>
        /// Implicit operator for converting any values to <see cref="BoxRef{T}"/>.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2225:OperatorOverloadsHaveNamedAlternates")]
        public static implicit operator BoxRef<T>(T value)
        {
            return new BoxRef<T>() { Value = value };
        }
    }
}
