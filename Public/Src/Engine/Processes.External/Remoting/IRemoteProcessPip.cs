// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Represents a process pip that executes remotely.
    /// </summary>
    public interface IRemoteProcessPip : IDisposable
    {
        /// <summary>
        /// Allows awaiting remote processing completion.
        /// </summary>
        /// <exception cref="TaskCanceledException">
        /// The caller-provided cancellation token was signaled or the object was disposed.
        /// </exception>
        Task<IRemoteProcessPipResult> Completion { get; }
    }
}
