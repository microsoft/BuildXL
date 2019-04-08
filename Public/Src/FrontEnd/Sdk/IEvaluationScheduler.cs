// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Abstraction that controls tasks scheduling for evaluation phase.
    /// </summary>
    public interface IEvaluationScheduler
    {
        /// <summary>
        /// Evaluate value using a given factory method.
        /// </summary>
        Task<object> EvaluateValue(Func<Task<object>> evaluateValueFunction);

        /// <summary>
        /// Token for cooperative cancellation.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Used by the interpreter to request early cancellation.
        /// </summary>
        void Cancel();
    }
}
