// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A Scope is a named container for variables.
    /// </summary>
    public interface IScope
    {
        /// <summary>
        /// Name of the scope (as such 'Arguments', 'Locals').
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The variables of this scope can be retrieved by passing the value of <code cref="IVariablesCommand.VariablesReference"/>.
        /// </summary>
        int VariablesReference { get; }

        /// <summary>
        /// If true, the number of variables in this scope is large or expensive to retrieve.
        /// </summary>
        bool Expensive { get; }
    }
}
