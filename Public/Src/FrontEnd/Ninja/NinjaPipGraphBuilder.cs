using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities;
using BuildXL.Pips;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.FrontEnd.Ninja
{
    internal sealed class NinjaPipGraphBuilder
    {
        private FrontEndContext m_context;
        private FrontEndHost m_frontEndHost;
        private readonly NinjaPipConstructor m_pipConstructor;

        /// <nodoc/>
        public NinjaPipGraphBuilder(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            AbsolutePath projectRoot,
            AbsolutePath specPath,
            QualifierId qualifierId,
            string frontEndName,
            bool suppressDebugFlags,
            IUntrackingSettings untrackingSettings)
        {
            Contract.Requires(context != null);
            Contract.Requires(frontEndHost != null);
            Contract.Requires(moduleDefinition != null);
            Contract.Requires(projectRoot.IsValid);
            Contract.Requires(specPath.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            m_context = context;
            m_frontEndHost = frontEndHost;

            m_pipConstructor = new NinjaPipConstructor(context, frontEndHost, frontEndName, moduleDefinition, qualifierId, projectRoot, specPath, suppressDebugFlags, untrackingSettings);
        }


        internal bool TrySchedulePips(IReadOnlyCollection<NinjaNode> filteredNodes, QualifierId qualifierId)
        {
            // We get this collection toposorted from the Json
            foreach (NinjaNode n in filteredNodes)
            {
                if (!m_pipConstructor.TrySchedulePip(n, qualifierId, out _))
                {
                    // Already logged 
                    return false;
                }
            }

            return true;
        }
    }
}
