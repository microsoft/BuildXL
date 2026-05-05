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

        /// <summary>
        /// Attempts to forcibly tear down the server end of the pipe to unblock the reader, regardless
        /// of whether any external process still holds a writer-end handle in the kernel.
        /// </summary>
        /// <returns>
        /// True if the disconnect attempt completed successfully. False if this reader implementation
        /// has no equivalent operation (for example, anonymous-pipe based readers always return false),
        /// or if the call observed a benign race with natural pipe teardown.
        /// </returns>
        /// <remarks>
        /// Intended as a last-resort unblock when EOF cannot be reached because a non-self process
        /// (typically a CREATE_BREAKAWAY_FROM_JOB descendant or a process that received the writer handle
        /// via DuplicateHandle) is keeping the writer end open. After a successful TryDisconnect the
        /// caller should re-await <see cref="CompletionAsync"/>; the reader's read loop will observe EOF
        /// (or an IOException) and complete promptly.
        ///
        /// Implementations must not throw. Benign races with natural pipe teardown
        /// (ObjectDisposedException, IOException, InvalidOperationException) are swallowed and reported
        /// as false.
        /// </remarks>
        bool TryDisconnect();
    }
}
