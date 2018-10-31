// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.Streams
{
    /// <summary>
    /// Target to receive the result of an async IO operation.
    /// </summary>
    /// <remarks>
    /// An example of a completion target might be a file stream. The target (stream) is responsible
    /// for understanding the meaning of a result when it arrives.
    /// </remarks>
    public interface IIOCompletionTarget
    {
        /// <summary>
        /// Completion callback for a previously-started async IO operation.
        /// </summary>
        void OnCompletion(FileAsyncIOResult asyncIOResult);
    }
}
