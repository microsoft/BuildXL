// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;

namespace BuildXL.Processes
{
    /// <summary>
    /// Interface for AsyncPipeReaders. Used for Legacy, Stream, and Baseline.
    /// </summary>
    internal interface IAsyncPipeReader : IDisposable
    {
        /// <summary>
        /// Begins read line.
        /// </summary>
        void BeginReadLine();

        /// <summary>
        /// Waits for completion.
        /// </summary>
        /// <param name="waitForEof">Wait for EOF if set to true.</param>
        Task CompletionAsync(bool waitForEof);
    }
}
