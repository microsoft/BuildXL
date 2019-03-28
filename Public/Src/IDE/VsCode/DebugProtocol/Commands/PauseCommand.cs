// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Pause request; value of the <see cref="IRequest.Command"/> field is "pause".
    ///
    /// The request suspenses the debuggee. The debug adapter will respond with a
    /// <code cref="IStoppedEvent"/> (event type 'pause') after a successful 'pause' command.
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface IPauseCommand : ICommand<IPauseResult>
    {
        /** Pause execution for this thread. */
        int ThreadId { get; }
    }

    /// <summary>
    /// Response to <see cref="IPauseCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IPauseResult { }
}
