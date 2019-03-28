// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Tracing
{
    /// <summary>
    /// Event data for <see cref="CriticalErrorsObserver.OnCriticalError"/> event.
    /// </summary>
    public sealed class CriticalErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Critical exception that occurred in a cache code.
        /// </summary>
        public Exception CriticalException { get; }

        /// <summary>
        /// Operation result that failed.
        /// </summary>
        public ResultBase Result { get; }

        /// <inheritdoc />
        public CriticalErrorEventArgs(ResultBase result)
        {
            Contract.Requires(result != null);
            Contract.Requires(result.IsCriticalFailure);

            CriticalException = result.Exception;
            Result = result;
        }
    }

    /// <summary>
    /// Event data for <see cref="CriticalErrorsObserver.OnRecoverableError"/> event.
    /// </summary>
    public sealed class RecoverableErrorEventArgs : EventArgs
    {
        /// <summary>
        /// Exception that occurred in a cache code.
        /// </summary>
        public Exception Exception { get; }

        /// <summary>
        /// Operation result that failed.
        /// </summary>
        public ResultBase Result { get; }

        /// <inheritdoc />
        public RecoverableErrorEventArgs(ResultBase result)
        {
            Contract.Requires(result != null);
            Contract.Requires(!result.IsCriticalFailure);
            Contract.Requires(result.HasException);

            Exception = result.Exception;
            Result = result;
        }
    }
}
