using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Ninja.Serialization;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Encapsulates a <see cref="NinjaGraph"/> with its corresponding <see cref="ModuleDefinition"/>
    /// </summary>
    public readonly struct NinjaGraphWithModuleDefinition
    {
        /// <nodoc/>
        public NinjaGraph Graph { get; }

        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }

        /// <nodoc />
        public NinjaGraphWithModuleDefinition(NinjaGraph graph, ModuleDefinition moduleDefinition) 
        {
            Contract.Requires(graph != null);
            Contract.Requires(moduleDefinition != null);

            Graph = graph;
            ModuleDefinition = moduleDefinition;
        }
    }
}
