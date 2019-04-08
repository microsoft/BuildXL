// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// StepIn request; value of the <see cref="IRequest.Command"/> field is "stepIn".
    ///
    /// The request starts the debuggee to run again for one step. The debug adapter will respond
    /// with a <code cref="IStoppedEvent"/> (reason 'step') after running the step.
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface IStepInCommand : ICommand<IStepInResult>
    {
        /** Continue execution for this thread. */
        int ThreadId { get; }
    }

    /// <summary>
    /// Response to <see cref="IStepInCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IStepInResult { }
}
