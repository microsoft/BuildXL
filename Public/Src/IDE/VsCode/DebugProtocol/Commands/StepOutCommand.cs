// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// StepIn request; value of the <see cref="IRequest.Command"/> field is "stepOut".
    ///
    /// The request starts the debuggee to leave the current function.
    /// The debug adapter will respond with a <code cref="IStoppedEvent"/> (reason 'step')
    /// after running the step.
    ///
    /// A response to this request is just an acknowledgement, so no body field is required.
    /// </summary>
    public interface IStepOutCommand : ICommand<IStepOutResult>
    {
        /** Continue execution for this thread. */
        int ThreadId { get; }
    }

    /// <summary>
    /// Response to <see cref="IStepOutCommand"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IStepOutResult { }
}
