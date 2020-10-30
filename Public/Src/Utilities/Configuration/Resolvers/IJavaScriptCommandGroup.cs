// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A sequence of commands that will be executed as a single unit.
    /// </summary>
    /// <remarks>
    /// The execution order will honor the sequence specified of commands specified. 
    /// A command in the sequence will be executed if the previous one succeeded.
    /// The commandName can be used for specifying dependencies as if it was a regular command.
    /// This represents the case of a command group where the execution semantics is provided by the JavaScript coordinator (e.g. Lage). For
    /// the one used in the case where BuildXL provides it, <see cref="IJavaScriptCommandGroupWithDependencies"/>
    /// </remarks>
    public interface IJavaScriptCommandGroup
    {
        /// <nodoc/>
        string CommandName { get; }

        /// <nodoc/>
        IReadOnlyList<string> Commands { get; }
    }
}
