// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Variables request; value of the <see cref="IRequest.Command"/> field is "variables".
    ///
    /// Retrieves all children for the given variable reference.
    /// </summary>
    public interface IVariablesCommand : ICommand<IVariablesResult>
    {
        /// <summary>
        /// The Variable reference.
        /// </summary>
        int VariablesReference { get; }
    }

    /// <summary>
    /// Response to<code cref="IVariablesCommand"/>.
    /// </summary>
    public interface IVariablesResult
    {
        /// <summary>
        /// All children for the given variable reference.
        /// </summary>
        IReadOnlyList<IVariable> Variables { get; }
    }
}
