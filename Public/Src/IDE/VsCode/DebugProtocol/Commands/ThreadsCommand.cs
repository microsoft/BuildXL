// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Thread request; value of the <see cref="IRequest.Command"/> field is "threads".
    ///
    /// The request retrieves a list of all threads.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:EmptyInterface")]
    public interface IThreadsCommand : ICommand<IThreadsResult> { }

    /// <summary>
    /// Response to <code cref="IThreadsCommand"/>.
    /// </summary>
    public interface IThreadsResult
    {
        /// <summary>
        /// All threads.
        /// </summary>
        IReadOnlyList<IThread> Threads { get; }
    }
}
