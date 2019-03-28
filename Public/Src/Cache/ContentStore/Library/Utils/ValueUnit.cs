// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    ///     This type is effectively 'void', but usable as a type parameter when a value type is needed.
    /// </summary>
    /// <remarks>
    ///     This is useful for generic methods dealing in tasks, since one can avoid having an overload
    ///     for both Task and Task{TResult}. One instead provides only a Task{TResult} overload, and
    ///     callers with a void result return Void.
    /// </remarks>
    [SuppressMessage(
        "Microsoft.Performance",
        "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes",
        Justification = "There is no point in comparing or hashing ValueUnit.")]
    public readonly struct ValueUnit
    {
        /// <summary>
        ///     Void unit type
        /// </summary>
        public static readonly ValueUnit Void = default(ValueUnit);
    }
}
