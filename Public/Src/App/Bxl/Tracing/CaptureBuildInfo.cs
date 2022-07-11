// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Collect information related to a build in a consistent fashion in CloudBuild, ADO for the purpose of telemetry.
    /// Fields to be collected about a build Infra, Org, Codebase, StageId, Label.
    /// </summary>
    /// <remarks>
    /// Below are the list of properties which capture the required information about the build for telemetry purpose.
    /// infra - identify the environment in which the build is run(CloudBuild, Azure DevOps).
    /// org - identify the orgnization triggering the build.
    /// </remarks>
    public class CaptureBuildInfo
    {
        /// <summary>
        /// Infra property key value.
        /// </summary>
        public const string InfraKey = "infra";

        /// <summary>
        /// Org property key value.
        /// </summary>
        public const string OrgKey = "org";

        /// <summary>
        /// ADO predefined variable to obtain the URI of the ADO organization.
        /// In CB the same environment variable is set in the GenericBuildRunner.
        /// The value of this env variable is parsed to obtain the organization information.
        /// </summary>
        public const string EnvVariableForOrg = "SYSTEM_TEAMFOUNDATIONCOLLECTIONURI";

        /// <summary>
        /// ADO predefined environment variable to identify the pipeline that's being run. 
        /// The presence of this variable indicates that the build is run on an Azure DevOps agent.
        /// </summary>
        public const string AdoEnvVariableForInfra = "BUILD_DEFINITIONNAME";

        /// <summary>
        /// This is the primary method in the class which is called by ComputeEnvironment(), to capture the build properties.
        /// </summary>
        /// <param name="configuration">This configuration object contains computed telemetry env properties and traceInfo flag fields.</param>
        /// <returns>The traceInfo Dictionary with build properties for is returned </returns>        
        public static Dictionary<string, string> CaptureTelemetryEnvProperties(IConfiguration configuration)
        {
            Dictionary<string, string> traceInfoProperties = new Dictionary<string, string>(configuration.Logging.TraceInfo, StringComparer.InvariantCultureIgnoreCase);
            if (!traceInfoProperties.ContainsKey(InfraKey))
            {
                string infraPropertyValue = GetInfra(configuration);
                traceInfoProperties.Add(InfraKey, infraPropertyValue);
            }

            if (!traceInfoProperties.ContainsKey(OrgKey))
            {
                string orgPropertyValue = GetOrg();
                if (!string.IsNullOrEmpty(orgPropertyValue))
                {
                    traceInfoProperties.Add(OrgKey, orgPropertyValue);
                }
            }

            return traceInfoProperties;
        }

        /// <summary>
        /// This method is used to set a build property called Infra.
        /// </summary>
        /// <param name="configuration">Configuration object has the InCloudBuild(), which set returns true only for CB env</param>  
        private static string GetInfra(IConfiguration configuration)
        {
            if (configuration.InCloudBuild())
            {
                return "cb";
            }
            else if (Environment.GetEnvironmentVariables().Contains(AdoEnvVariableForInfra))
            {
                return "ado";
            }

            return "dev";
        }

        /// <summary>
        /// This method is used to set a property called Org in the EnvString for telemetry purpose. The method parses the URL and capture the organization name.
        /// </summary>
        private static string GetOrg()
        {
            string orgUnParsedURL = Environment.GetEnvironmentVariable(EnvVariableForOrg);
            if (!string.IsNullOrEmpty(orgUnParsedURL))
            {
                // According to the AzureDevOps documentation, there are two kinds of ADO URL's
                // New format(https://dev.azure.com/{organization}) & legacy format(https://{organization}.visualstudio.com). 
                // Based on this information, the name of the organization is extracted using the below logic.
                var match = Regex.Match(orgUnParsedURL, "(?<=dev\\.azure\\.com/)(.*?)(?=/)");
                if (!match.Success)
                {
                    match = Regex.Match(orgUnParsedURL, "(?<=https://)(.*?)(?=\\.visualstudio\\.com)");
                    if (!match.Success)
                    {
                        return null;
                    }
                }

                return match.Groups[0].Value;
            }

            return null;            
        }
    }
}
