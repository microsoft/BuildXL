// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Pips.Builders;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Creates a pip based on a <see cref="JavaScriptProject"/> based on Lage
    /// </summary>
    internal sealed class LagePipConstructor : JavaScriptPipConstructor
    {
        private readonly ILageResolverSettings m_resolverSettings;
        private readonly FrontEndContext m_context;

        /// <summary>
        /// The arguments passed to node that identifies the lage server being used
        /// </summary>
        internal const string LageServerArgumentBreakaway = "lage-server";

        /// <nodoc/>
        public LagePipConstructor(
            FrontEndContext context,
            FrontEndHost frontEndHost,
            ModuleDefinition moduleDefinition,
            LageConfiguration lageConfiguration,
            ILageResolverSettings resolverSettings,
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment,
            IEnumerable<string> userDefinedPassthroughVariables,
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IEnumerable<JavaScriptProject> allProjectsToBuild) 
        : base(context, frontEndHost, moduleDefinition, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables, customCommands, allProjectsToBuild)
        {
            m_resolverSettings = resolverSettings;
            m_context = context;
        }

        /// <inheritdoc/>
        protected override IEnumerable<AbsolutePath> GetResolverSpecificAllowedSourceReadsScopes()
        {
            var specificReadScopes = new List<AbsolutePath>();
            
            // Lage and npm locations are common scopes that pips read into
            if (m_resolverSettings.LageLocation is not null)
            {
                specificReadScopes.Add(m_resolverSettings.LageLocation.Value.Path.GetParent(PathTable));
            }

            if (m_resolverSettings.NpmLocation is not null)
            {
                specificReadScopes.Add(m_resolverSettings.NpmLocation.Value.Path.GetParent(PathTable));
            }

            return base.GetResolverSpecificAllowedSourceReadsScopes().Union(specificReadScopes);
        }

        /// <inheritdoc/>
        protected override void ConfigureProcessBuilder(ProcessBuilder processBuilder, JavaScriptProject project, IReadOnlySet<JavaScriptProject> transitiveDependencies)
        {
            base.ConfigureProcessBuilder(processBuilder, project, transitiveDependencies);

            // In some scenarios Lage spawns a server process 'lage-server' that serves a variety of requests. This process needs to break away from the sandbox.
            var lageServerBreakaway = new BreakawayChildProcess() {
                ProcessName = PathAtom.Create(m_context.StringTable, OperatingSystemHelper.IsWindowsOS ? "node.exe" : "node"),
                RequiredArguments = LageServerArgumentBreakaway
            };

            processBuilder.ChildProcessesToBreakawayFromSandbox = processBuilder.ChildProcessesToBreakawayFromSandbox.Append(lageServerBreakaway).ToArray();
        }
    }
}
