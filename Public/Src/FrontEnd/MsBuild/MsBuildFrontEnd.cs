// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <summary>
    /// Resolver frontend that can schedule MsBuild projects using the static graph API from MsBuild.
    /// </summary>
    public sealed class MsBuildFrontEnd : FrontEnd<MsBuildWorkspaceResolver>
    {
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

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { MsBuildWorkspaceResolver.MsBuildResolverName };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new MsBuildResolver(Host, Context, Name);
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
    }
}
