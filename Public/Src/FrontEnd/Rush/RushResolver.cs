// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.JavaScript;
using BuildXL.FrontEnd.JavaScript.ProjectGraph;
using BuildXL.FrontEnd.Rush.ProjectGraph;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Core;

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

            // If the rush location is specified, it has to be valid
            if (rushResolverSettings.RushLocation?.IsValid == false)
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), "The specified location for 'rush' is invalid.");
                return false;
            }

            // Just being defensive here, rush location and rush-lib base location cannot be specified together. This should be enforced by the DScript type checker already.
            if (rushResolverSettings.RushLocation?.IsValid == true && rushResolverSettings.RushLibBaseLocation?.IsValid == true)
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), "Both rush location and rush-lib base location cannot be specified together.");
                return false;
            }

            // Just being defensive here, rush-lib does not support passing a Rush command. This should be enforced by the DScript type checker already.
            if (rushResolverSettings.RushLibBaseLocation?.IsValid == true && rushResolverSettings.RushCommand != null)
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), "Passing rush commands is not available when using rush-lib.");
                return false;
            }

            // Just being defensive here, rush-lib does not support passing additional Rush parameters. This should be enforced by the DScript type checker already.
            if (rushResolverSettings.RushLibBaseLocation?.IsValid == true && rushResolverSettings.AdditionalRushParameters != null)
            {
                Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), "Passing additional parameters is not available when using rush-lib.");
                return false;
            }

            // Additional parameters are optional, but if they are specified, parameter names have to start with a dash and not contain spaces.
            if (rushResolverSettings.AdditionalRushParameters != null)
            {
                foreach (var parameter in rushResolverSettings.AdditionalRushParameters)
                {
                    if (!IsValidAdditionalParameter(parameter, out var error))
                    {
                        Tracing.Logger.Log.InvalidRushResolverSettings(Context.LoggingContext, Location.FromFile(rushResolverSettings.File.ToString(Context.PathTable)), error);
                        return false;
                    }
                }
            }

            if (!base.ValidateResolverSettings(rushResolverSettings))
            {
                return false;
            }

            return true;
        }

        private static bool IsValidAdditionalParameter(DiscriminatingUnion<string, IAdditionalNameValueParameter> parameter, out string error)
        {
            // Parameter names need to start with a dash and not contain spaces or quotes.
            string paramName = parameter.GetValue() is string flagParam ? flagParam : ((IAdditionalNameValueParameter)parameter.GetValue()).Name;
            if (!(paramName.StartsWith('-') && !(new[] { ' ', '\"'}.Any(c => paramName.Contains(c)))))
            {
                error = $"Invalid additional parameter '{paramName}'. It should start with '-' and not contain spaces or quotes.";
                return false;
            }

            error = string.Empty;
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
