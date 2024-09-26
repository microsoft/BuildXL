// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Lage.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.FrontEnd.Lage
{
    /// <summary>
    /// Resolver for Lage based builds.
    /// </summary>
    public class LageResolver : JavaScriptResolver<LageConfiguration, LageResolverSettings>
    {
        /// <nodoc/>
        public LageResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName) : base(host, context, frontEndName)
        {
        }

        /// <inheritdoc/>
        protected override IProjectToPipConstructor<JavaScriptProject> CreateGraphToPipGraphConstructor(
            FrontEndHost host, 
            ModuleDefinition moduleDefinition, 
            LageResolverSettings resolverSettings, 
            LageConfiguration configuration, 
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment, 
            IEnumerable<string> userDefinedPassthroughVariables, 
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IReadOnlyCollection<JavaScriptProject> allProjectsToBuild)
        {
            return new LagePipConstructor(Context, host, moduleDefinition, configuration, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables, customCommands, allProjectsToBuild);
        }
    }
}
