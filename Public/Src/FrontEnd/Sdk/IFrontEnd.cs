// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using NotNullAttribute = JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// A front end is able to create resolvers for a set of supported resolver kinds.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public interface IFrontEnd
    {
        /// <summary>
        /// Returns the supported resolvers
        /// </summary>
        /// <returns>The resulting collection is not null or empty.</returns>
        [NotNull]
        IReadOnlyCollection<string> SupportedResolvers { get; }

        /// <summary>
        /// Initializes the frontend
        /// </summary>
        void InitializeFrontEnd([NotNull]FrontEndHost host, [NotNull]FrontEndContext context, [NotNull]IConfiguration frontEndConfiguration);

        /// <summary>
        /// Creates a resolver for a given kind. The resolver must be part of the front end
        /// supported resolvers.
        /// </summary>
        [NotNull]
        IResolver CreateResolver([NotNull]string kind);

        /// <summary>
        /// Creates a resolver for a given kind. The resolver must be part of the front end
        /// supported resolvers.
        /// </summary>
        bool TryCreateWorkspaceResolver([NotNull] IResolverSettings resolverSettings, [NotNull] out IWorkspaceModuleResolver workspaceResolver);

        /// <summary>
        /// Allows a frontend to log its statistics after evaluation
        /// </summary>
        void LogStatistics(Dictionary<string, long> statistics);
    }
}
