// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines the root module
    /// </summary>
    public interface IRootModuleConfiguration : IModuleConfiguration
    {
        /// <summary>
        /// The defined modules
        /// </summary>
        [CanBeNull]
        IReadOnlyDictionary<ModuleId, IModuleConfiguration> ModulePolicies { get; }

        /// <summary>
        /// List of path fragments of tools using search path directory enumeration whereby only files with accessed file names
        /// are important for directory membership fingerprinting.
        /// </summary>
        [CanBeNull]
        IReadOnlyList<RelativePath> SearchPathEnumerationTools { get; }

        /// <summary>
        /// List of path fragments of tools using its own incrementality
        /// </summary>
        [CanBeNull]
        IReadOnlyList<RelativePath> IncrementalTools { get; }
    }
}
