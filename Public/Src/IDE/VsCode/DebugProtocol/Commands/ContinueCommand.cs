// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Continue request; value of the <see cref="IRequest.Command"/> field is "continue".
    ///
    /// The request starts the debuggee to run again.
    /// </summary>
    public interface IContinueCommand : ICommand<IContinueResult>
    {
        /// <summary>
        /// Continue execution for the specified thread (if possible).
        /// If the backend cannot continue on a single thread but will continue on all threads,
        /// it should set the <code cref="IContinueResult.AllThreadsContinued"/>
        /// attribute in the response to true.
        /// </summary>
        int? ThreadId { get; }
    }

    /// <summary>
    /// Response to <code cref="IContinueCommand"/>.
    /// </summary>
    public interface IContinueResult
    {
        /// <summary>
        /// If true, the continue request has ignored the specified thread and continued all threads instead.
        /// If this attribute is missing a value of 'true' is assumed for backward compatibility.
        /// </summary>
        bool AllThreadsContinued { get; }
    }
}
