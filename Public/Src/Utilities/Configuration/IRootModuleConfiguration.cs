// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Core;

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
        [MaybeNull]
        IReadOnlyDictionary<ModuleId, IModuleConfiguration> ModulePolicies { get; }

        /// <summary>
        /// List of path fragments of tools using search path directory enumeration whereby only files with accessed file names
        /// are important for directory membership fingerprinting.
        /// </summary>
        [MaybeNull]
        IReadOnlyList<RelativePath> SearchPathEnumerationTools { get; }

        /// <summary>
        /// List of path fragments of tools using its own incrementality
        /// </summary>
        [MaybeNull]
        IReadOnlyList<RelativePath> IncrementalTools { get; }
    }
}
