// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A Variable is a name/value pair.
    /// If the value is structured (has children), a handle is provided to retrieve the
    /// children with the <code cref="IVariablesCommand"/>.
    /// </summary>
    public interface IVariable
    {
        /// <summary>
        /// The variable's name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// The variable's value. For structured objects this can be a multi line text, e.g.,
        /// for a function the body of a function.
        /// </summary>
        string Value { get; }

        /// <summary>
        /// If <code cref="VariablesReference"/> is &gt; 0, the variable is structured and its
        /// children can be retrieved by passing <code cref="IVariablesCommand.VariablesReference"/>
        /// to the <code cref="IVariablesCommand"/>.
        /// </summary>
        int VariablesReference { get; }
    }
}
