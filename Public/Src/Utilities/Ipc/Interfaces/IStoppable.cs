// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Provides methods to implement a protocol for stopping a client/server.
    /// The protocol is similar to that of System.Threading.Tasks.Dataflow:
    /// <see cref="RequestStop"/> should be called first, and then await on
    /// <see cref="Completion"/>.
    /// </summary>
    public interface IStoppable : IDisposable
    {
        /// <summary>
        /// Task to wait on for the completion of this object.
        /// </summary>
        [Pure]
        Task Completion { get; }

        /// <summary>
        /// Requests shut down, causing this instance to immediatelly stop serving new requests.
        /// Any pending requests, however, should be processed to completion.
        /// </summary>
        void RequestStop();
    }
}
