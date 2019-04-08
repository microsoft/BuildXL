// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Pips
{
    /// <summary>
    /// Event arguments for callback when Pip has finished executing
    /// </summary>
    public sealed class PipCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates an instance
        /// </summary>
        public PipCompletedEventArgs(PipId pipId, Task<PipResult> pipRunTask)
        {
            Contract.Requires(pipId.IsValid);
            PipId = pipId;
            PipRunTask = pipRunTask;
        }

        /// <summary>
        /// Pip that finished executing
        /// </summary>
        public PipId PipId { get; private set; }

        /// <summary>
        /// A completed Task (where the task may have completed successfully, faulted, or was canceled) indicating the result of a
        /// Pip execution
        /// </summary>
        public Task<PipResult> PipRunTask { get; private set; }
    }
}
