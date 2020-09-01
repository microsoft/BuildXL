// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// Resolver for Rush based builds.
    /// </summary>
    public class RushResolver : JavaScriptResolver<RushConfiguration, RushResolverSettings>
    {
        /// <nodoc/>
        public RushResolver(
            FrontEndHost host,
            FrontEndContext context,
            string frontEndName) : base(host, context, frontEndName)
        {
        }

        /// <inheritdoc/>
        protected override bool ValidateResolverSettings(RushResolverSettings rushResolverSettings)
        {
            string rushJson = rushResolverSettings.Root.Combine(Context.PathTable, "rush.json").ToString(Context.PathTable);
            if (!FileUtilities.Exists(rushJson))
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)),
                    $"Rush configuration file 'rush.json' was not found under the specified root '{rushJson}'.");
                return false;
            }

            // If the rush-lib base location is specified, it has to be valid
            if (rushResolverSettings.RushLibBaseLocation?.IsValid == false)
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), "The specified rush-lib base location is invalid.");
                return false;
            }

            if (!base.ValidateResolverSettings(rushResolverSettings))
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc/>
        protected override IProjectToPipConstructor<JavaScriptProject> CreateGraphToPipGraphConstructor(
            FrontEndHost host, 
            ModuleDefinition moduleDefinition, 
            RushResolverSettings resolverSettings, 
            RushConfiguration configuration, 
            IEnumerable<KeyValuePair<string, string>> userDefinedEnvironment, 
            IEnumerable<string> userDefinedPassthroughVariables, 
            IReadOnlyDictionary<string, IReadOnlyList<JavaScriptArgument>> customCommands,
            IReadOnlyCollection<JavaScriptProject> allProjectsToBuild)
        {
            return new RushPipConstructor(Context, host, moduleDefinition, configuration, resolverSettings, userDefinedEnvironment, userDefinedPassthroughVariables, customCommands, allProjectsToBuild);
        }
    }
}
