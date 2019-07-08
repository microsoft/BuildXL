// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Resolver frontend that can schedule MsBuild projects using the static graph API from MsBuild.
    /// </summary>
    public sealed class MsBuildFrontEnd : IFrontEnd
    {
        private FrontEndContext m_context;
        private FrontEndHost m_host;

        /// <summary>
        /// Used to make sure all MsBuild resolvers are configured to load the MsBuild assemblies
        /// from the same locations
        /// </summary>
        /// <remarks>
        /// Order matters, since that may result in different resolved assemblies
        /// </remarks>
        private readonly List<AbsolutePath> m_loadedMsBuildAssemblyLocations = new List<AbsolutePath>();

        /// <nodoc />
        public const string Name = MsBuildWorkspaceResolver.MsBuildResolverName;

        /// <nodoc/>
        public MsBuildFrontEnd()
        {
        }

        /// <inheritdoc/>
        public void InitializeFrontEnd(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            m_host = host;
            m_context = context;
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<string> SupportedResolvers => new[] { MsBuildWorkspaceResolver.MsBuildResolverName };

        /// <inheritdoc/>
        public IResolver CreateResolver(string kind)
        {
            return new MsBuildResolver(m_host, m_context, Name);
        }

        /// <summary>
        /// Validates that all resolvers are configured such that they all load the required MsBuild assemblies from the same
        /// locations
        /// </summary>
        internal bool TryValidateMsBuildAssemblyLocationsAreCoordinated(IEnumerable<AbsolutePath> assemblyPathsToLoad)
        {
            Contract.Requires(assemblyPathsToLoad != null);

            lock(m_loadedMsBuildAssemblyLocations)
            {
                // If this is the first time a resolver is reporting the load of MsBuild assemblies, we just store the result
                if (m_loadedMsBuildAssemblyLocations.Count == 0)
                {
                    m_loadedMsBuildAssemblyLocations.AddRange(assemblyPathsToLoad);
                    return true;
                }

                // We validate that the paths to load the assemblies from are the same
                // Order matters, since a different order may result in different dlls found
                return m_loadedMsBuildAssemblyLocations.SequenceEqual(assemblyPathsToLoad);
            }
        }

        /// <inheritdoc />
        public void LogStatistics(Dictionary<string, long> statistics)
        {
        }
    }
}
