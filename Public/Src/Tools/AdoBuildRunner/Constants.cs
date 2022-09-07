// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.AdoBuildRunner
{
    /// <summary>
    /// Collection of constants used for orchestration, mostly VSTS agent environment variables
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Name of the environment variable that contains the build id
        /// </summary>
        public const string BuildIdVarName = "BUILD_BUILDID";

        /// <summary>
        /// Name of the environment variable that contains the team project
        /// </summary>
        public const string TeamProjectVarName = "SYSTEM_TEAMPROJECT";

        /// <summary>
        /// Name of the environment variable that contains the team project id
        /// </summary>
        public const string TeamProjectIdVarName = "SYSTEM_TEAMPROJECTID";

        /// <summary>
        /// Name of the environment variable that contains the vsts server uri
        /// </summary>
        public const string ServerUriVarName = "SYSTEM_TEAMFOUNDATIONSERVERURI";

        /// <summary>
        /// Name of the environment variable that contains the PAT token to access VSTS
        /// </summary>
        public const string AccessTokenVarName = "SYSTEM_ACCESSTOKEN";

        /// <summary>
        /// Name of the environment variable that contains the job position in the phase
        /// </summary>
        public const string JobsPositionInPhaseVarName = "SYSTEM_JOBPOSITIONINPHASE";

        /// <summary>
        /// Name of the environment variable that contains the name of the VSTS Agent
        /// </summary>
        public const string AgentNameVarName = "AGENT_NAME";

        /// <summary>
        /// Name of the environment variable that contains the machine name of the VSTS Agent
        /// </summary>
        public const string AgentMachineNameVarName = "AGENT_MACHINENAME";

        /// <summary>
        /// Name of the environment variable that contains the sources directory
        /// </summary>
        public const string SourcesDirectoryVarName = "BUILD_SOURCESDIRECTORY";

        /// <summary>
        /// Name of the environment variable that contains the total number of jobs in the phase
        /// </summary>
        public const string TotalJobsInPhaseVarName = "SYSTEM_TOTALJOBSINPHASE";

        /// <summary>
        /// Name of the variable of the id of the timeline of the build
        /// </summary>
        public const string TimelineIdVarName = "SYSTEM_TIMELINEID";

        /// <summary>
        /// Name of the variable of the plan id of the build
        /// </summary>
        public const string PlanIdVarName = "SYSTEM_PLANID";

        /// <summary>
        /// Name of the variable of the Url of the build repository
        /// </summary>
        public const string RepositoryUrlVariableName = "BUILD_REPOSITORY_URI";

        /// <summary>
        /// Name of the variable of the current task's display name
        /// </summary>
        public const string TaskDisplayNameVariableName = "SYSTEM_TASKDISPLAYNAME";

        /// <summary>
        /// Name of the variable of the job attempt number
        /// </summary>
        public const string JobAttemptVariableName = "SYSTEM_JOBATTEMPT";

        /// <summary>
        /// Variable indicating the current agent type
        /// </summary>
        public const string MachineType = "MachineType";

        /// <summary>
        /// Hostname of the agent
        /// </summary>
        public const string MachineHostName = "MachineHostName";

        /// <summary>
        /// Name of the task variable used to communicate the machine IPV4 address
        /// </summary>
        public const string MachineIpV4Address = "MachineIpV4Address";

        /// <summary>
        /// Name of the task variable used to communicate the machine IPV4 address
        /// </summary>
        public const string MachineIpV6Address = "MachineIpV6Address";

        /// <summary>
        /// Name of the task variable used to communicate to the workers if the build failed
        /// </summary>
        public const string BuildStatus = "BuildStatus";

        /// <summary>
        /// Value used to communicate to the workers that the build has not started
        /// </summary>
        public const string BuildStatusNotFinished = "BuildStatusNotFinished";

        /// <summary>
        /// Value used to communicate to the workers that the build has succeeded
        /// </summary>
        public const string BuildStatusSuccess = "BuildStatusSuccess";
        
        /// <summary>
        /// Value used to communicate to the workers that the build has failed
        /// </summary>
        public const string BuildStatusFailure = "BuildStatusFailure";

        /// <summary>
        /// The port used to establish GRPC based connections for distributed builds
        /// </summary>
        public const int MachineGrpcPort = 6979;


        /// <summary>
        /// The port used to establish GRPC based connections for distributed builds
        /// </summary>
        public const int OrchestratorFailedWorkerReturnCode = 21;

        /// <summary>
        /// The maximum time an agent waits for the other agents to get ready before failing
        /// </summary>
        public const int DefaultMaximumWaitForWorkerSeconds = 600;

        /// <summary>
        /// The maximum time an agent waits for the other agents to get ready before failing
        /// </summary>
        public const string MaximumWaitForWorkerSecondsVariableName = "MaximumWaitForWorkerSeconds";

        /// <summary>
        /// The time the agent waits before re-checking if the other agents are ready
        /// </summary>
        public const int PollRetryPeriodInSeconds = 20;
    }
}
