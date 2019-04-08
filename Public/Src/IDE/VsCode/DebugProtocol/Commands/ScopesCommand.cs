// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Scopes request; value of the <see cref="IRequest.Command"/> field is "scopes".
    ///
    /// The request returns the variable scopes for a given stackframe ID.
    /// </summary>
    public interface IScopesCommand : ICommand<IScopesResult>
    {
        /// <summary>
        /// Retrieve the scopes for this stackframe.
        /// </summary>
        int FrameId { get; }
    }

    /// <summary>
    /// Response to <code cref="IScopesCommand"/>.
    /// </summary>
    public interface IScopesResult
    {
        /// <summary>
        /// The scopes of the stackframe. If the array has length zero, there are no scopes available.
        /// </summary>
        IReadOnlyList<IScope> Scopes { get; }
    }
}
