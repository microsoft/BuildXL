// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    /// A base class that represents an error event args.
    /// </summary>
    public abstract class ErrorEventArgs : EventArgs
    {
        /// <summary>
        /// An exception that occurred in a cache code.
        /// </summary>
        protected readonly Exception _exception;

        /// <summary>
        /// Operation result that failed.
        /// </summary>
        public ResultBase Result { get; }

        /// <inheritdoc />
        public ErrorEventArgs(ResultBase result)
        {
            Contract.Requires(result != null);
            Contract.Requires(result.Exception != null);

            _exception = result.Exception;
            Result = result;
        }
    }

    /// <summary>
    /// Event data for <see cref="CriticalErrorsObserver.OnCriticalError"/> event.
    /// </summary>
    public sealed class CriticalErrorEventArgs : ErrorEventArgs
    {
        /// <summary>
        /// Critical exception that occurred in a cache code.
        /// </summary>
        public Exception CriticalException => _exception;


        /// <inheritdoc />
        public CriticalErrorEventArgs(ResultBase result)
            : base (result)
        {
            Contract.Requires(result.IsCriticalFailure);
        }
    }

    /// <summary>
    /// Event data for <see cref="CriticalErrorsObserver.OnRecoverableError"/> event.
    /// </summary>
    public sealed class RecoverableErrorEventArgs : ErrorEventArgs
    {
        /// <summary>
        /// Exception that occurred in a cache code.
        /// </summary>
        public Exception Exception => _exception;

        /// <inheritdoc />
        public RecoverableErrorEventArgs(ResultBase result)
            : base(result)
        {
            Contract.Requires(!result.IsCriticalFailure);
        }
    }
}
