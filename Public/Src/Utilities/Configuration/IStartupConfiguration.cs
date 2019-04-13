// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The information needed to startup
    /// </summary>
    public interface IStartupConfiguration
    {
        /// <summary>
        /// The additional configuration files that supply extra modules
        /// </summary>
        AbsolutePath ConfigFile { get; }

        /// <summary>
        /// The additional configuration files that supply extra config
        /// </summary>
        IReadOnlyList<AbsolutePath> AdditionalConfigFiles { get; }

        /// <summary>
        /// The overrides for environment variables
        /// </summary>
        // TODO: Consider if we should just rename AllowedEnvironmentVars and support default values for them.
        [NotNull]
        IReadOnlyDictionary<string, string> Properties { get; }

        /// <summary>
        /// Qualifiers controlling what flavor to build
        /// </summary>
        // TODO: We probably want a way to specify an instance as well, perhaps overload the parsing. Use a named qualifier if starting with alphanumeric. Use an instance when starting with "{"?
        [NotNull]
        IReadOnlyList<string> QualifierIdentifiers { get; }

        /// <summary>
        /// The initial implicit filters. This is used as a shortcut instead of using the full filtering syntax
        /// </summary>
        [NotNull]
        IReadOnlyList<string> ImplicitFilters { get; }

        /// <summary>
        /// The Host information of the machine currently running
        /// </summary>
        [NotNull]
        IHost CurrentHost { get; }
    }
}
