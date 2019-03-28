// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides ability to create a value wrapping another value of a specific type
    /// </summary>
    /// <typeparam name="TValueBase">the base type of values wrapped by the factory</typeparam>
    /// <typeparam name="TArgs">type type of the additional arguments</typeparam>
    /// <typeparam name="TResult">the result type</typeparam>
    public interface IBoxFactory<in TValueBase, in TArgs, out TResult>
    {
        /// <summary>
        /// Creates a value wrapping another value of a specific type
        /// </summary>
        /// <typeparam name="TValue">the type of value which is wrapped</typeparam>
        /// <param name="value">the value which is wrapped</param>
        /// <param name="additionalArguments">the additional arguments used to construct the result</param>
        /// <returns>the wrapper for the value</returns>
        TResult CreateBox<TValue>(TValue value, TArgs additionalArguments) where TValue : TValueBase;
    }

    /// <summary>
    /// Provides ability to create a value wrapping another value of a specific type
    /// </summary>
    /// <typeparam name="TArgs">type type of the additional arguments</typeparam>
    /// <typeparam name="TResult">the result type</typeparam>
    public interface IBoxFactory<in TArgs, out TResult>
    {
        /// <summary>
        /// Creates a value wrapping another value of a specific type
        /// </summary>
        /// <typeparam name="TValue">the type of value which is wrapped</typeparam>
        /// <param name="value">the value which is wrapped</param>
        /// <param name="additionalArguments">the additional arguments used to construct the result</param>
        /// <returns>the wrapper for the value</returns>
        TResult CreateBox<TValue>(TValue value, TArgs additionalArguments);
    }
}
