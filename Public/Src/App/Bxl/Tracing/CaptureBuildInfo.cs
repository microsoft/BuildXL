// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;
using BuildXL;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Collect information related to a build in a consistent fashion in CloudBuild, ADO for the purpose of telemetry.
    /// Fields to be collected about a build Infra, Org, Codebase, StageId, Label.
    /// </summary>
    public class CaptureBuildInfo
    {
        /// <remarks>
        /// Infra build property key value.
        /// </remarks>
        public const string InfraKey = "infra";

        /// <summary>
        /// This method is used to set a build property called Infra.
        /// </summary>
        /// <param name="configuration">Configuration object</param>  
        /// <remarks>
        /// Infra - identify the environment in which the build is run(CloudBuild, Azure DevOps).
        /// </remarks>
        
        public static string GetInfra(IConfiguration configuration)
        {
            if (configuration.InCloudBuild())
            {
                return "cloudbuild";
            }
            else if (Environment.GetEnvironmentVariables().Contains("BUILD_DEFINITIONNAME"))
            {
                return "ado";
            }
            else
            {
                return "dev";
            }
        }
    }
}
