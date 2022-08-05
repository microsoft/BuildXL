// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;

namespace BuildXL
{
    /// <summary>
    /// Collect information related to a build in a consistent fashion in CloudBuild, ADO for the purpose of telemetry.
    /// Fields to be collected about a build Infra, Org, Codebase, StageId, Label.
    /// These fields have been moved to CaptureBuildProperties.cs for better accessibility.
    /// </summary>
    /// <remarks>
    /// Below are the list of properties which capture the required information about the build for telemetry purpose.
    /// infra - identify the environment in which the build is run(CloudBuild, Azure DevOps).
    /// org - identify the orgnization triggering the build.
    /// codebase - identifies the code or product that's being built. This will typically be the name of the Git repository.
    /// buildEntity - identifies the BuildEntity(queue/pipeline) used to build and deploy the codebase.
    /// stageId - identifies the invocation of BXL in the stage(sequence) - Enlist, Meta, Product, Compliance and Prep build.
    /// </remarks>
    public class CaptureBuildInfo
    {        
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
        /// ADO pre-defined environment variable to obtain the name of the repository triggering the build.
        /// </summary>
        public const string AdoPreDefinedVariableForCodebase = "BUILD_REPOSITORY_NAME";

        /// <summary>
        /// ADO pre-defined environment variable to obtain the id of the pipeline which was used to build the code.
        /// </summary>
        public const string AdoPreDefinedVariableForBuildEntity = "SYSTEM_DEFINITIONID";

        /// <summary>
        /// Represents the cloudbuildqueue property which is passed as a cmd line argument in a CB environment.
        /// </summary>
        public const string CloudBuildQueue = "cloudBuildQueue";

        /// <summary>
        /// This is the primary method in the class which is called by ComputeEnvironment(), to capture the build properties.
        /// </summary>
        /// <param name="configuration">This configuration object contains computed telemetry env properties and traceInfo flag fields.</param>
        /// <returns>The traceInfo Dictionary with build properties for is returned </returns>        
        public static Dictionary<string, string> CaptureTelemetryEnvProperties(IConfiguration configuration)
        {
            Dictionary<string, string> traceInfoProperties = new Dictionary<string, string>(configuration.Logging.TraceInfo, StringComparer.InvariantCultureIgnoreCase);
            if (!traceInfoProperties.ContainsKey(CaptureBuildProperties.InfraKey))
            {
                string infraPropertyValue = GetInfra(configuration);
                traceInfoProperties.Add(CaptureBuildProperties.InfraKey, infraPropertyValue);
            }

            if (!traceInfoProperties.ContainsKey(CaptureBuildProperties.OrgKey))
            {
                string orgPropertyValue = GetOrg();
                if (!string.IsNullOrEmpty(orgPropertyValue))
                {
                    traceInfoProperties.Add(CaptureBuildProperties.OrgKey, orgPropertyValue);
                }
            }

            if (!traceInfoProperties.ContainsKey(CaptureBuildProperties.CodeBaseKey))
            {
                string codebasePropertyValue = GetCodebase();
                if (!string.IsNullOrEmpty(codebasePropertyValue))
                {
                    traceInfoProperties.Add(CaptureBuildProperties.CodeBaseKey, codebasePropertyValue);
                }
            }

            if (!traceInfoProperties.ContainsKey(CaptureBuildProperties.BuildEntityKey))
            {
                string buildEntityPropertyValue = GetBuildEntity(traceInfoProperties);
                if (!string.IsNullOrEmpty(buildEntityPropertyValue))
                {
                    traceInfoProperties.Add(CaptureBuildProperties.BuildEntityKey, buildEntityPropertyValue);
                }
            }

            if (!traceInfoProperties.ContainsKey(CaptureBuildProperties.StageIdKey))
            {
                string stageIdPropertyValue = GetStageId(configuration);
                if (!string.IsNullOrEmpty(stageIdPropertyValue))
                {
                    traceInfoProperties.Add(CaptureBuildProperties.StageIdKey, stageIdPropertyValue);
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

        /// <summary>
        /// This method is used to set a build property called Codebase in the EnvString for telemetry purpose in an ADO environment. 
        /// For CB, the codebase information is passed as a part of traceInfo.
        /// This method captures the required information from the ADO pre-defined variable "Build_Repository_Name"
        /// This variable gives the name of the triggering repository.
        /// </summary>
        private static string GetCodebase()
        {
            // For ADO the build property BUILD_REPOSITORY_NAME is used to extract the codebase information.
            if (Environment.GetEnvironmentVariables().Contains(AdoPreDefinedVariableForCodebase))
            {
                return Environment.GetEnvironmentVariable(AdoPreDefinedVariableForCodebase);
            }

            return null;
        }

        /// <summary>
        /// This method is used to set the build property called BuildEntity in the EnvString for telemetry purpose in an ADO environment.
        /// In CB env the information about the buildqueue is passed via traceInfo. This info is used to set the property buildEntity to the buildQueue name.
        /// This method captures the required information from the ADO pre-defined variable "System_DefinitionId"
        /// This variable gives the id of the pipeline that is used to build the codebase.
        /// </summary>
        /// <returns></returns>
        private static string GetBuildEntity(Dictionary<string, string> traceInfoProperties)
        {
            if (traceInfoProperties.ContainsKey(CloudBuildQueue))
            {
                return traceInfoProperties.GetValueOrDefault(CloudBuildQueue);
            }

            if (Environment.GetEnvironmentVariables().Contains(AdoPreDefinedVariableForBuildEntity))
            {
                return Environment.GetEnvironmentVariable(AdoPreDefinedVariableForBuildEntity);
            }

            return null;
        }

        /// <summary>
        /// This method is used to set a build property called StageID in the EnvString for telemetry purpose.
        /// In Office, every build has three separate builds called Product, Meta, Enlistment build.Each of this build invokes BXL separately. This information is obtained below.
        /// In general each of this build is considered as a stage(sequence) and the name of that stage is assigned to the property "stageId".
        /// Similarly all JS builds have three stages, Prep, Compliance and Build(main/real build). This information is passed from CB via traceInfo.
        /// As of now Windows has only a single BXL build.
        /// </summary>
        private static string GetStageId(IConfiguration configuration)
        {
            switch(configuration.Logging.Environment)
            {
                case ExecutionEnvironment.OfficeEnlistmentBuildDev:
                case ExecutionEnvironment.OfficeEnlistmentBuildLab:
                    return "enlist";
                case ExecutionEnvironment.OfficeMetaBuildDev:
                case ExecutionEnvironment.OfficeMetaBuildLab:
                    return "meta";
                case ExecutionEnvironment.OfficeProductBuildDev:
                case ExecutionEnvironment.OfficeProductBuildLab:
                    return "product";
                default: 
                    return null;
            }    
        }
    }
}
