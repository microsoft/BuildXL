// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Wrapper for instance which should dispose another resource as a part of its disposal
    /// NOTE: <see cref="Value"/> is disposed prior to inner disposable
    /// </summary>
    public class Disposable<T> : IDisposable
        where T : IDisposable
    {
        /// <summary>
        /// The inner disposable
        /// </summary>
        private readonly IDisposable m_innerDisposable;

        /// <summary>
        /// The result value
        /// </summary>
        public readonly T Value;

        /// <summary>
        /// Constructs a new disposable
        /// </summary>
        /// <param name="value">the result value</param>
        /// <param name="innerDisposable">the inner disposable which is disposed with this instance</param>
        public Disposable(T value, IDisposable innerDisposable)
        {
            Value = value;
            m_innerDisposable = innerDisposable;
        }

        /// <summary>
        /// Disposes the value followed by the inner disposable
        /// </summary>
        public void Dispose()
        {
            using (m_innerDisposable)
            using (Value)
            {
                // Do nothing. These using blocks are only used to dispose the instances
            }
        }

        /// <summary>
        /// Creates a new disposable which wraps the given value with current instance as the inner disposable.
        /// </summary>
        public Disposable<TResult> Chain<TResult>(TResult result)
            where TResult : IDisposable
        {
            return new Disposable<TResult>(result, this);
        }

        /// <summary>
        /// Creates a new disposable which wraps the value created by the given selector with current instance as the inner disposable.
        /// </summary>
        public Disposable<TResult> ChainSelect<TResult>(Func<T, TResult> selector, bool disposeOnError = true)
            where TResult : IDisposable
        {
            try
            {
                return new Disposable<TResult>(selector(Value), this);
            }
            catch
            {
                if (disposeOnError)
                {
                    Dispose();
                }

                throw;
            }
        }
    }

    /// <summary>
    /// Utilities for creating <see cref="Disposable{T}"/> instances
    /// </summary>
    public static class Disposable
    {
        /// <summary>
        /// Creates a <see cref="Disposable{T}"/>
        /// </summary>
        public static Disposable<T> Create<T>(T value, IDisposable innerDisposable = null)
            where T : IDisposable
        {
            return new Disposable<T>(value, innerDisposable);
        }
    }
}
