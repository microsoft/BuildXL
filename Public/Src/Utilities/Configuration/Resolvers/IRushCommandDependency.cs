// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A Rush command can have 'local' dependencies, meaning dependencies on commands of the same project (e.g. test depends on build)
    /// or specify a dependency on a command from all its direct dependencies.
    /// </summary>
    public interface IRushCommandDependency
    {
        /// <summary>
        /// 'local' or 'package'
        /// </summary>
        string Kind { get; }
        
        /// <nodoc/>
        string Command { get; }

        /// <summary>
        /// Whether kind is 'local' (as opposed to 'package')
        /// </summary>
        bool IsLocalKind();
    }
}
