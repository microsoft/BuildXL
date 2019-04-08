// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a lazily-evaluated value and allows checking whether the value has already begun evaluating
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class LazyEx<T>
    {
        /// <summary>
        /// Value is either:
        /// Delegate used to initialize Lazy
        /// LazyInternalLock for started value which has not completed
        /// BoxedValue containing pointer to result of delegate
        /// LazyInternalExceptionHolder containing pointer to exception raised by delegate
        /// </summary>
        private object m_value;

        /// <summary>
        /// Base class for values which have been started
        /// </summary>
        private abstract class StartedValue
        {
        }

        /// <summary>
        /// Base class for values which have been started
        /// </summary>
        private abstract class CompletedValue : StartedValue
        {
        }

        /// <summary>
        /// Wrapper class for created value
        /// </summary>
        private sealed class Boxed : CompletedValue
        {
            public readonly T Value;

            internal Boxed(T value)
            {
                Value = value;
            }
        }

        /// <summary>
        /// Lock type
        /// </summary>
        private sealed class LazyInternalLock : StartedValue
        {
            public readonly Func<T> ValueFactory;

            internal LazyInternalLock(Func<T> valueFactory)
            {
                ValueFactory = valueFactory;
            }
        }

        /// <summary>
        /// Wrapper class to wrap the excpetion thrown by the value factory
        /// </summary>
        private sealed class LazyInternalExceptionHolder : CompletedValue
        {
            internal readonly ExceptionDispatchInfo ExceptionInfo;

            internal LazyInternalExceptionHolder(Exception ex)
            {
                ExceptionInfo = ExceptionDispatchInfo.Capture(ex);
            }
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="valueFactory">the value factory for creating the value</param>
        public LazyEx(Func<T> valueFactory)
        {
            Contract.Requires(valueFactory != null);

            m_value = valueFactory;
        }

        /// <summary>
        /// Gets the evaluated value blocking until value is created if value is already evaluating
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        public T Value
        {
            get
            {
                T value;
                if (!TryCreateValue(out value, creationLockTimeout: Timeout.Infinite))
                {
                    // The only case where TryCreateValue returns false with infinite timeout
                    // is if this thread already has the lock meaning re-entrant call by valueFactory
                    throw new InvalidOperationException(Strings.ErrorLazyReentrancy);
                }

                return value;
            }
        }

        /// <summary>
        /// Attempts to lock and create the value using the value factory.
        /// </summary>
        /// <param name="value">the evaluated value</param>
        /// <param name="creationLockTimeout">the timeout to wait to acquire lock for creating value</param>
        /// <returns>true if the lazy initialization is already complete or the current thread created the value.
        /// false if the value is currently being evaluated.</returns>
        public bool TryCreateValue(out T value, int creationLockTimeout)
        {
            Contract.Requires(creationLockTimeout >= 0 || creationLockTimeout == Timeout.Infinite);

            if (TryGetValueOrThrowException(out value))
            {
                return true;
            }

            var capturedValue = m_value;
            LazyInternalLock internalLock = capturedValue as LazyInternalLock;
            Func<T> valueFactory = capturedValue as Func<T>;
            if (valueFactory != null)
            {
                Interlocked.CompareExchange(ref m_value, new LazyInternalLock(valueFactory), valueFactory);
                internalLock = m_value as LazyInternalLock;
            }

            if (internalLock != null)
            {
                if (Monitor.IsEntered(internalLock))
                {
                    // Lock is already taken by this thread
                    return false;
                }

                bool lockTaken = Monitor.TryEnter(internalLock, creationLockTimeout);
                if (lockTaken)
                {
                    try
                    {
                        if (!IsCompleted)
                        {
                            m_value = new Boxed(internalLock.ValueFactory());
                        }
                    }
                    catch (Exception ex)
                    {
                        m_value = new LazyInternalExceptionHolder(ex);
                        throw;
                    }
                    finally
                    {
                        Monitor.Exit(internalLock);
                    }
                }
            }

            return TryGetValueOrThrowException(out value);
        }

        /// <summary>
        /// Gets the value of the Lazy&lt;T&gt; for debugging display purposes.
        /// </summary>
        internal T ValueForDebugDisplay => !IsValueCreated ? default(T) : ((Boxed)m_value).Value;

        private bool TryGetValueOrThrowException(out T value)
        {
            Boxed boxed = m_value as Boxed;
            if (boxed != null)
            {
                value = boxed.Value;
                return true;
            }

            LazyInternalExceptionHolder exHolder = m_value as LazyInternalExceptionHolder;
            exHolder?.ExceptionInfo.Throw();

            value = default(T);
            return false;
        }

        /// <summary>
        /// Gets whether the lazy has been evaluated and the value is created sucessfully
        /// </summary>
        public bool IsValueCreated => m_value is Boxed;

        /// <summary>
        /// Gets whether the lazy has been evaluated and successful or faulted
        /// </summary>
        public bool IsCompleted => m_value is CompletedValue;

        /// <summary>
        /// Indicates if the underlying value has started evaluating
        /// </summary>
        public bool IsStarted
        {
            get
            {
                var capturedValue = m_value;
                return capturedValue is StartedValue;
            }
        }
    }
}
