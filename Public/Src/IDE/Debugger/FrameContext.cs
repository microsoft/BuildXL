// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.FrontEnd.Script.Debugger
{
    /// <summary>
    ///     Simple memento class for storing a frame context.
    ///
    ///     Represents a state that is associated with scope handles (which are integers are by
    ///     which scopes are referred to in back-and-forth communication with a debugger front end).
    ///
    ///     Deeply immutable.
    /// </summary>
    public sealed class FrameContext
    {
        /// <summary>Id of the corresponding thread (globally unique).</summary>
        public int ThreadId { get; }

        /// <summary>
        ///     Corresponding stack frame index of the referenced scope.
        ///     Unique per thread id, i.e., (ThreadId, FrameIndex) is unique globally.
        /// </summary>
        public int FrameIndex { get; }

        /// <summary>Corresponding DScript evaluation context.</summary>
        public ThreadState ThreadState { get; }

        /// <summary>Constructor.</summary>
        public FrameContext(int threadId, int frameId, ThreadState threadState)
        {
            ThreadId = threadId;
            FrameIndex = frameId;
            ThreadState = threadState;
        }
    }
}
