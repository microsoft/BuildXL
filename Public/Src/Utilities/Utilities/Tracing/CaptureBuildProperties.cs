// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Collect information related to a build in a consistent fashion in CloudBuild, ADO for the purpose of telemetry.
    /// Fields to be collected about a build infra, org, codebase, stageid, label, pipelineid, cloudBuildQueue.
    /// </summary>
    /// <remarks>
    /// Below are the list of properties which capture the required information about the build for telemetry purpose.
    /// infra - identify the environment in which the build is run (CloudBuild, Azure DevOps).
    /// org - identify the organization triggering the build.
    /// codebase - identifies the code or product that's being built. This will typically be the name of the Git repository. It will vary in the other source control mechanism.
    /// pipelineid - identifies the pipeline used to build and deploy the codebase.
    /// cloudBuildQueue - identifies the CB buildqueue used to build and deploy the codebase.    
    /// stageid - identifies the invocation of BXL in the stage (sequence) - Enlist, Meta, Product, Compliance and Prep build.
    /// label - Customer specific identifier which can be used to identify build uniquely.
    /// </remarks>
    public class CaptureBuildProperties
    {
        /// <summary>
        /// infra property key name.
        /// </summary>
        public const string InfraKey = "infra";

        /// <summary>
        /// org property key name.
        /// </summary>
        public const string OrgKey = "org";

        ///<summary>
        /// codebase property key name
        /// </summary>
        public const string CodeBaseKey = "codebase";

        /// <summary>
        /// pipelineid property key name.
        /// In ADO this build structure refers to the ID of the build pipeline.
        /// </summary>
        public const string PipelineIdKey = "pipelineid";
        
        /// <summary>
        /// Build Id of the run that launched this build.
        /// </summary>
        public const string AdoBuildIdKey = "adobuildid";

        /// <summary>
        /// cloudBuildQueue property key name.
        /// In CB this build structure refers to the CB queue name.
        /// This information is already passed via traceInfo.
        /// </summary>
        public const string BuildQueueKey = "cloudBuildQueue";

        /// <summary>
        /// stageid - Identifies the invocation of bxl.exe in the sequence: Enlist, Meta, Product, Compliance, etc. 
        /// Every Office build has three builds -  Enlist, Meta, Product. Each of these builds invokes BXL separately.
        /// Every JS build has three builds - Prep, Compliance and the main build (real build). Similar to above each of these builds invokes BXL separately.
        /// The intention of this property is to identify the stage (sequence) of the build invoking BXL.
        /// </summary>
        public const string StageIdKey = "stageid";

        /// <summary>
        /// label - This property is used as an additional identifier when trying to distinguish between different .Summary.md files in ADO, where each build produces this file but has similar name.
        /// This value is passed via /traceInfo.
        /// </summary>
        public const string LabelKey = "label";
    }
}
