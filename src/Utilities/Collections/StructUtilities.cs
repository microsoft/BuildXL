// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Utilities for implementing value types.
    /// </summary>
    public static class StructUtilities
    {
        /// <summary>
        /// Helper for implementing <see cref="object.Equals(object)"/> for a value type.
        /// This handles the necessary unboxing (since the right-hand side is either an unrelated
        /// reference or value type, therefore unequal, or a boxed <typeparamref name="T"/>)
        /// and then defers to <see cref="IEquatable{T}.Equals(T)"/>.
        /// </summary>
        /// <remarks>
        /// Remember that implementing <see cref="IEquatable{T}"/> is extremely important
        /// for a value type to participate in generic collections without boxing.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Equals<T>(T left, object right) where T : struct, IEquatable<T>
        {
            if (!(right is T))
            {
                return false;
            }

            return left.Equals((T)right);
        }

        /// <summary>
        /// This is an overload for <see cref="Equals{T}"/> that prevents bad calls.
        /// Without it, one could use a value type for the right-hand-side and accidentally box it.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "left")]
        [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "right")]
        [ContractVerification(false)]
        public static bool Equals<TLeft, TRight>(TLeft left, TRight right) where TLeft : struct where TRight : struct
        {
            Contract.Requires(false, "Don't call StructUtilities.Equals with a value type on the right! That makes silly boxes.");
            return false;
        }
    }
}
