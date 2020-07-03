// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A JavaScript command where depedencies on other commands can be explicitly provided
    /// E.g. { command: "test", dependsOn: { kind: "local", command: "build"} }
    /// makes the 'test' script depend on the 'build' script
    /// of the same project.
    /// Dependencies on other commands of direct dependencies can be specified as well. For example:
    /// {command: "localize", dependsOn: {kind: "project", command: "build"}} makes the 'localize' script depend on the 'build' script
    /// of all of the project declared dependencies
    /// </summary>
    public interface IJavaScriptCommand
    {
        /// <nodoc/>
        string Command { get; }

        /// <nodoc/>
        IReadOnlyList<IJavaScriptCommandDependency> DependsOn {get;}
    }
}
