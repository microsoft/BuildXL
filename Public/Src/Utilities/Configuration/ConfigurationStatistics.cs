// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Configuration statistics use for building the pip graph
    /// </summary>
    public readonly struct ConfigurationStatistics
    { 
        /// <summary>
        /// Resolver kinds involved in graph construction
        /// </summary>
        public string ResolverKinds { get; }

        /// <nodoc/>
        public ConfigurationStatistics(IEnumerable<string> resolverKinds)
        {
            Contract.RequiresNotNull(resolverKinds);
            ResolverKinds = string.Join(", ", resolverKinds);
        }
    }
}
