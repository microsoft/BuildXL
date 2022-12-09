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
    /// Fields to be collected about a build infra, org, codebase, stageid, label, pipelineid, cloudbuildqueue.
    /// These fields have been moved to CaptureBuildProperties.cs for better accessibility.
    /// </summary>
    /// <remarks>
    /// Below are the list of properties which capture the required information about the build for telemetry purpose.
    /// infra - identify the environment in which the build is run(CloudBuild, Azure DevOps).
    /// org - identify the orgnization triggering the build.
    /// codebase - identifies the code or product that's being built. This will typically be the name of the Git repository.
    /// pipelineid - identifies the pipeline used to build and deploy the codebase.
    /// cloudbuildqueue - identifies the CB buildqueue used to build and deploy the codebase.
    /// stageid - identifies the invocation of BXL in the stage(sequence) - Enlist, Meta, Product, Compliance and Prep build.
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
        public const string AdoPreDefinedVariableForPipelineId = "SYSTEM_DEFINITIONID";

        /// <summary>
        /// ADO pre-defined environment variable to obtain the id of the ADO build which ran BuildXL.
        /// </summary>
        public const string AdoPreDefinedVariableForBuildId = "BUILD_BUILDID";

        /// <summary>
        /// ADO predefined variable to obtain the name of the project that contains the ADO build.
        /// </summary>
        public const string AdoPreDefinedVariableForProject = "SYSTEM_TEAMPROJECT";

        /// <summary>
        /// ADO predefined variable to obtain the requester of the ADO build.
        /// </summary>
        public const string AdoPreDefinedVariableForRequester = "BUILD_REQUESTEDFORID";

        /// <summary>
        /// ADO pre-defined environment variable to obtain the job id.
        /// </summary>
        public const string AdoPreDefinedVariableForJobId = "SYSTEM_JOBID";

        /// <summary>
        /// This is the primary method in the class which is called by ComputeEnvironment(), to capture the build properties.
        /// </summary>
        /// <param name="configuration">This configuration object contains computed telemetry env properties and traceInfo flag fields.</param>
        /// <returns>The traceInfo Dictionary with build properties for is returned </returns>        
        public static Dictionary<string, string> CaptureTelemetryEnvProperties(IConfiguration configuration)
        {
            var traceInfoProperties = new Dictionary<string, string>(configuration.Logging.TraceInfo, StringComparer.InvariantCultureIgnoreCase);

            // The organization name
            CaptureNewProperty(traceInfoProperties, CaptureBuildProperties.OrgKey, GetOrg);

            // The name of the triggering repository.
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.CodeBaseKey, AdoPreDefinedVariableForCodebase);

            // The id of the pipeline that is used to build the codebase.
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.PipelineIdKey, AdoPreDefinedVariableForPipelineId);

            // The build id for the pipeline run that triggers this build (ADO only)
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.AdoBuildIdKey, AdoPreDefinedVariableForBuildId);

            // The project name for the pipeline run that triggers this build (ADO only)
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.AdoProjectKey, AdoPreDefinedVariableForProject);

            // The requester name for this build (ADO only)
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.AdoRequesterKey, AdoPreDefinedVariableForRequester);

            // The job id for the pipeline run that triggers this build (ADO only)
            CaptureNewPropertyFromEnvironment(traceInfoProperties, CaptureBuildProperties.AdoJobIdKey, AdoPreDefinedVariableForJobId);

            // See GetStageId and GetInfra
            CaptureNewProperty(traceInfoProperties, CaptureBuildProperties.StageIdKey, () => GetStageId(configuration));
            CaptureNewProperty(traceInfoProperties, CaptureBuildProperties.InfraKey, () => GetInfra(configuration));

            return traceInfoProperties;
        }

        /// <summary>
        /// If the key is not present in the dictionary, this method captures a property using the provided producer and adds it to it 
        /// </summary>
        private static void CaptureNewProperty(Dictionary<string, string> properties, string key, Func<string> valueProducer)
        {
            if (!properties.ContainsKey(key))
            {
                var value = valueProducer();
                if (!string.IsNullOrEmpty(value))
                {
                    properties.Add(key, value);
                }
            }
        }

        private static void CaptureNewPropertyFromEnvironment(Dictionary<string, string> properties, string key, string envVariableName)
        {
            CaptureNewProperty(properties, key, () => Environment.GetEnvironmentVariable(envVariableName));
        }


        /// <summary>
        /// This method is used to set a build property called infra.
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
        /// This method is used to set a property called org in the EnvString for telemetry purpose. The method parses the URL and capture the organization name.
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
        /// This method is used to set a build property called stageid in the EnvString for telemetry purpose.
        /// In Office, every build has three separate builds called Product, Meta, Enlistment build.Each of this build invokes BXL separately. This information is obtained below.
        /// In general each of this build is considered as a stage(sequence) and the name of that stage is assigned to the property "stageid".
        /// Similarly all JS builds have three stages, Prep, Compliance and Build(main/real build). This information is passed from CB via traceInfo.
        /// As of now Windows has only a single BXL build.
        /// </summary>
        private static string GetStageId(IConfiguration configuration)
        {
            switch (configuration.Logging.Environment)
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
