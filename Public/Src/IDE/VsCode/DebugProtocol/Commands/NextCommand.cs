// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Next request; value of the <see cref="IRequest.Command"/> field is "next".
    ///
    /// The request starts the debuggee to run again for one step.
    /// The debug adapter will respond with a <code cref="IStoppedEvent"/> (event type 'next')
    /// after running the step.
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface INextCommand : ICommand<INextResult>
    {
        /** Continue execution for this thread. */
        int ThreadId { get; }
    }

    /// <summary>
    /// Response to <see cref="INextCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface INextResult { }
}
