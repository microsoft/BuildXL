// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Tracker for operations which allows starting new operations
    /// </summary>
    public interface IOperationTracker
    {
        /// <summary>
        /// Starts a new operation
        /// </summary>
        OperationContext StartOperation(OperationKind kind, LoggingContext loggingContext);
    }

    /// <summary>
    /// Null operation tracker which does not track operations (for testing use only)
    /// </summary>
    internal sealed class NullOperationTracker : IOperationTracker
    {
        /// <inheritdoc />
        public OperationContext StartOperation(OperationKind kind, LoggingContext loggingContext)
        {
            return new OperationContext(loggingContext, operation: null);
        }
    }
}
