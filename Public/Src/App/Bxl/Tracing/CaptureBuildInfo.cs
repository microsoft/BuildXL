// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.ToolSupport;
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

        private const string ADONewUrlFormatRegex = @"(?<=dev\.azure\.com/)(.*?)(?=/)";

        private const string ADOLegacyUrlFormatRegex = @"(?<=https://)(.*?)(?=\.visualstudio\.com)";

        /// <summary>
        /// This is the primary method in the class which is called by ComputeEnvironment(), to capture the build properties.
        /// </summary>
        /// <param name="configuration">This configuration object contains computed telemetry env properties and traceInfo flag fields.</param>
        /// <param name="gitRemoteRepoUrl">This value is used to extract the codebase and org value when the required env vars are not available. </param>
        /// <returns>The traceInfo Dictionary with build properties for is returned </returns>        
        public static Dictionary<string, string> CaptureTelemetryEnvProperties(IConfiguration configuration, string gitRemoteRepoUrl = null)
        {
            var traceInfoProperties = new Dictionary<string, string>(configuration.Logging.TraceInfo, StringComparer.InvariantCultureIgnoreCase);

            // The organization name
            CaptureNewProperty(traceInfoProperties, CaptureBuildProperties.OrgKey, getOrg);

            // The name of the triggering repository.
            CaptureNewProperty(traceInfoProperties, CaptureBuildProperties.CodeBaseKey, getCodebase);

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

            // This method is used to set the 'org' property in the EnvString for telemetry purposes.
            // This method first attempts to capture the org name from the environment variable.
            // If the environment variable is not set or is invalid, and if the gitRemoteRepoUrl is available,
            // the method extracts the org value from the gitRemoteRepoUrl.
            string getOrg()
            {
                var orgFromEnvVar = ExtractOrgFromUrl(Environment.GetEnvironmentVariable(EnvVariableForOrg));
                return !string.IsNullOrEmpty(orgFromEnvVar) ? orgFromEnvVar : ExtractOrgFromUrl(gitRemoteRepoUrl);
            }

            // This method is used to set the 'codebase' property in the EnvString for telemetry purposes.
            // This method first attempts to capture the codebase value from the env var.
            // If the env var is not set, and if the gitRemoteRepoUrl is available,
            // the method extracts the codebase value from it.
            string getCodebase()
            {
                var codeBase = Environment.GetEnvironmentVariable(AdoPreDefinedVariableForCodebase);

                if (!string.IsNullOrEmpty(codeBase))
                {
                    return codeBase;
                }

                if (!string.IsNullOrEmpty(gitRemoteRepoUrl))
                {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                    try
                    {
                        var uri = new Uri(gitRemoteRepoUrl);
                        return uri.Segments.Last();
                    }
                    catch (Exception)
                    {
                    }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
                }

                return null;
            }
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
        private static string GetInfra(IConfiguration configuration) => configuration.Infra switch
        {
            Infra.Ado => "ado",
            Infra.CloudBuild => "cb",
            Infra.Developer => "dev",
            _ => throw new NotSupportedException($"Not expected infra value: '{configuration.Infra}' ({(byte)configuration.Infra})."),
        };

        /// <summary>
        /// Determines the current infrastructure based on the environment and provided command line arguments.
        /// </summary>
        public static Infra DetermineInfra(CommandLineUtilities cl)
        {
            bool inCb = false;
            foreach (CommandLineUtilities.Option opt in cl.Options)
            {
                if (string.Equals(opt.Name, "inCloudBuild", StringComparison.OrdinalIgnoreCase))
                {
                    // Look through all command line args; the last arg is used (to keep it in line with how we handle args parsing/processing).
                    inCb = CommandLineUtilities.ParseBooleanOption(opt);
                }
            }

            if (inCb)
            {
                return Infra.CloudBuild;
            }
            else if (Environment.GetEnvironmentVariables().Contains(AdoEnvVariableForInfra))
            {
                return Infra.Ado;
            }

            return Infra.Developer;
        }

        /// <summary>
        /// Extracts the organization name from the given URL based on predefined domain patterns.
        /// </summary>
        private static string ExtractOrgFromUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                // According to the AzureDevOps documentation and telemetry observations, there are two primary formats for Azure DevOps URLs:
                // - New format: https://dev.azure.com/{organization}
                // - Legacy format: https://{organization}.visualstudio.com
                // These patterns are used to extract the organization info as required.
                var match = Regex.Match(url, ADONewUrlFormatRegex);
                if (!match.Success)
                {
                    match = Regex.Match(url, ADOLegacyUrlFormatRegex);
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
