// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// StackTrace request; value of the <see cref="IRequest.Command"/> field is "stackTrace".
    ///
    /// The request returns a stacktrace from the current execution state.
    /// </summary>
    public interface IStackTraceCommand : ICommand<IStackTraceResult>
    {
        /// <summary>
        /// Retrieve the stacktrace for this thread.
        /// </summary>
        int ThreadId { get; }

        /// <summary>
        /// The index of the first frame to return; if omitted frames start at 0.
        /// </summary>
        int? StartFrame { get; }

        /// <summary>
        /// The index of the first frame to return; if omitted frames start at 0.
        /// </summary>
        int? Levels { get; }
    }

    /// <summary>
    /// Response to <code cref="IStackTraceCommand"/>.
    /// </summary>
    public interface IStackTraceResult
    {
        /// <summary>
        /// The frames of the stackframe. If the array has length zero, there are no stackframes available.
        /// This means that there is no location information available.
        /// </summary>
        IReadOnlyList<IStackFrame> StackFrames { get; }

        /// <summary>
        /// The total number of frames available.
        /// </summary>
        int? TotalFrames { get; }
    }
}
