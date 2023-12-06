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
        /// Name of the environment variable that contains the build id of the build that triggered this one
        /// </summary>
        public const string TriggeredByBuildIdVarName = "BUILD_TRIGGEREDBY_BUILDID";

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
        /// Name of the environment variable that contains the job id (an identifier for a job unique within a pipeline)
        /// </summary>
        public const string JobIdVariableName = "SYSTEM_JOBID";

        /// <summary>
        /// Name of the environment variable that contains the name of the job (the identifier used to express dependencies, different than the display name)
        /// </summary>
        public const string JobNameVariableName = "SYSTEM_JOBNAME";

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
        /// Name of the variable of the Url of the ADO collection (e.g. 
        /// </summary>
        public const string CollectionUrlVariableName = "SYSTEM_COLLECTIONURI";

        /// <summary>
        /// Name of the variable of the current task's display name
        /// </summary>
        public const string TaskDisplayNameVariableName = "SYSTEM_TASKDISPLAYNAME";

        /// <summary>
        /// Name of the variable of the job attempt number
        /// </summary>
        public const string JobAttemptVariableName = "SYSTEM_JOBATTEMPT";

        /// <summary>
        /// Name of the variable of the source version (i.e., commit id)
        /// </summary>
        public const string SourceVersionVariableName = "BUILD_SOURCEVERSION";

        /// <summary>
        /// Name of the variable of the source branch
        /// </summary>
        public const string SourceBranchVariableName = "BUILD_SOURCEBRANCH";

        /// <summary>
        /// Name of the variable of the target pr branch
        /// </summary>
        public const string TargetBranchVariableName = "BUILD_TARGETBRANCH";

        /// <nodoc />
        public const string TriggeringAdoBuildIdParameter = "BuildXLTriggeringAdoBuildId";

        /// <summary>
        /// The port used to establish GRPC based connections for distributed builds
        /// </summary>
        public const int MachineGrpcPort = 6979;

        /// <summary>
        /// The maximum time an agent waits for the other agents to get ready before failing
        /// </summary>
        public const int DefaultMaximumWaitForWorkerSeconds = 600;

        /// <summary>
        /// The maximum time an agent waits for the other agents to get ready before failing
        /// </summary>
        public const string MaximumWaitForWorkerSecondsVariableName = "MaximumWaitForWorkerSeconds";

        /// <summary>
        /// When set to "true", makes worker always succeed.
        /// </summary>
        /// <remarks>
        /// When workers run in the same pipeline as the orchestrator, we don't want to make the pipeline
        /// fail just because of a worker's failure. Note that this will make workers unable to participate in retries,
        /// but we still don't have a mechanism to trigger an automatic retry of a different job when the orchestrator
        /// job is retried, especially if it happens on a separate stage.
        /// </remarks>
        public const string WorkerAlwaysSucceeds = "AdoBuildRunnerWorkerAlwaysSucceeds";

        /// <summary>
        /// If set to the value "1", gRPC encryption using 1ES HP certificates won't be enabled for the build
        /// </summary>
        public const string DisableEncryptionVariableName = "AdoBuildRunnerDisableEncryption";

        /// <summary>
        /// Indicates the distributed build role taken by the build runner
        /// Should be either "Orchestrator" or "Worker"
        /// </summary>
        public const string AdoBuildRunnerPipelineRole = "AdoBuildRunnerWorkerPipelineRole";

        /// <summary>
        /// Disambiguates builds that might run as part of the same pipeline
        /// </summary>
        public const string AdoBuildRunnerInvocationKey = "AdoBuildRunnerInvocationKey";

        /// <summary>
        /// The time the agent waits before re-checking if the other agents are ready
        /// </summary>
        public const int PollRetryPeriodInSeconds = 20;
    }
}
