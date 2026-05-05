// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.ToolSupport;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// Operational mode for the CloudTest client.
    /// </summary>
    public enum CloudTestMode
    {
        CreateSession,
        UpdateDynamicJob,
        CancelSession,
        GenerateSessionConfig,
        GenerateUpdateDynamicJobConfig,
        WaitForSessionCompletion,
    }

    /// <summary>
    /// Represents a job definition with a name and an optional explicit ID.
    /// When the ID is null, a unique GUID will be auto-generated.
    /// </summary>
    public record JobDefinition(string Name, string Id = null);

    /// <summary>
    /// Parsed command-line arguments for the CloudTest client.
    /// </summary>
    public sealed class CloudTestClientArgs : CommandLineUtilities
    {
        #region Common arguments (all modes)

        /// <summary>
        /// The operational mode.
        /// </summary>
        public CloudTestMode Mode { get; }

        /// <summary>
        /// Path to the JSON file containing the request body.
        /// Not required for CancelSession mode.
        /// </summary>
        public string BodyFile { get; }

        /// <summary>
        /// Name of the environment variable that holds the bearer token.
        /// </summary>
        public string TokenEnvVar { get; }

        /// <summary>
        /// Path to the output file for logs and errors.
        /// </summary>

        /// <summary>
        /// CloudTest tenant name.
        /// </summary>
        public string Tenant { get; }

        /// <summary>
        /// Session ID. Resolved at runtime from either /sessionId or /sessionIdFile.
        /// Required for UpdateDynamicJob and CancelSession modes.
        /// For CreateSession, this is extracted from the submit response.
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>
        /// Path to the session ID file.
        /// For CreateSession: output file where the session ID is written.
        /// For UpdateDynamicJob/CancelSession: input file from which the session ID is read.
        /// </summary>
        public string SessionIdFile { get; }

        /// <summary>
        /// Overall timeout for the operation. Default is 5 minutes.
        /// </summary>
        public TimeSpan Timeout { get; }


        #endregion

        #region GenerateSessionConfig arguments


        /// <summary>Display name for the session.</summary>
        public string DisplayName { get; }

        /// <summary>User alias submitting the session.</summary>
        public string User { get; }

        /// <summary>Build drop location URL.</summary>
        public string BuildDropLocation { get; }

        /// <summary>VM SKU (e.g. Standard_D4s_v3).</summary>
        public string Sku { get; }

        /// <summary>VM image (e.g. ubuntu22.04).</summary>
        public string Image { get; }

        /// <summary>Maximum number of VMs to allocate in parallel.</summary>
        public int MaxResources { get; }

        /// <summary>
        /// List of job definitions. Each entry has a name and an optional explicit ID.
        /// Jobs added via /jobName have an auto-generated ID; jobs added via /jobIdAndName use the specified ID.
        /// </summary>
        public List<JobDefinition> Jobs { get; } = new List<JobDefinition>();

        /// <summary>Path to write the generated session configuration JSON.</summary>
        public string ConfigOutputFile { get; }

        /// <summary>Whether job result caching is enabled. Default: false.</summary>
        public bool CacheEnabled { get; }

        /// <summary>Azure DevOps project ID (SYSTEM_TEAMPROJECTID). When set, a VstsContext is included in the generated config.</summary>
        public string AdoProjectId { get; }

        /// <summary>Azure DevOps collection URI, e.g. https://dev.azure.com/myorg/ (SYSTEM_COLLECTIONURI).</summary>
        public string AdoCollectionUri { get; }

        /// <summary>Azure DevOps build ID (BUILD_BUILDID).</summary>
        public string AdoBuildId { get; }

        /// <summary>Name of the environment variable holding the ADO OAuth token (e.g. SYSTEM_ACCESSTOKEN).</summary>
        public string AdoAccessTokenEnvVar { get; }


        #endregion

        #region GenerateUpdateDynamicJobConfig arguments


        /// <summary>Specific job ID for the dynamic job update.</summary>
        public string JobId { get; private set; }

        /// <summary>Path to the session config file for resolving a job name to a job ID.</summary>
        public string SessionConfigPath { get; }

        /// <summary>Relative path within the drop containing this job's test files.</summary>
        public string TestFolder { get; }

        /// <summary>Path to executable on the worker VM. Supports [WorkingDirectory] macro.</summary>
        public string JobExecutable { get; }

        /// <summary>Test framework type: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest.</summary>
        public string TestExecutionType { get; }

        /// <summary>Arguments passed to the test executable.</summary>
        public string JobArguments { get; }

        /// <summary>Result parser type: TRX, JUnit, TAEF, NUnitXml, TAP.</summary>
        public string TestParserType { get; }

        /// <summary>Max job duration in HH:MM:SS format.</summary>
        public string JobTimeout { get; }

        /// <summary>Per-test-case timeout in HH:MM:SS format.</summary>
        public string TestCaseTimeout { get; }

        /// <summary>Hashes of test inputs for caching. Aggregated into a single hash by the tool.</summary>
        public List<string> TestDependencyHashes { get; } = new List<string>();

        /// <summary>Job priority (lower = higher priority).</summary>
        public int? Priority { get; }

        #endregion

        /// <nodoc />
        public CloudTestClientArgs(string[] args)
            : base(args)
        {
            string mode = null;
            int timeoutMinutes = 5;

            foreach (Option opt in Options)
            {
                switch (opt.Name.ToUpperInvariant())
                {
                    case "MODE":
                        mode = opt.Value;
                        break;
                    case "BODYFILE":
                        BodyFile = opt.Value;
                        break;
                    case "TOKENENVVAR":
                        TokenEnvVar = opt.Value;
                        break;
                    case "TENANT":
                        Tenant = opt.Value;
                        break;
                    case "SESSIONID":
                        SessionId = opt.Value;
                        break;
                    case "SESSIONIDFILE":
                        SessionIdFile = opt.Value;
                        break;
                    case "TIMEOUT":
                        if (!int.TryParse(opt.Value, out timeoutMinutes) || timeoutMinutes <= 0)
                        {
                            throw Error($"Invalid timeout value '{opt.Value}'. Must be a positive integer (minutes).");
                        }
                        break;
                    case "DISPLAYNAME":
                        DisplayName = opt.Value;
                        break;
                    case "USER":
                        User = opt.Value;
                        break;
                    case "BUILDDROPLOCATION":
                        BuildDropLocation = opt.Value;
                        break;
                    case "SKU":
                        Sku = opt.Value;
                        break;
                    case "IMAGE":
                        Image = opt.Value;
                        break;
                    case "MAXRESOURCES":
                        if (!int.TryParse(opt.Value, out var mr) || mr <= 0)
                        {
                            throw Error($"Invalid maxResources value '{opt.Value}'. Must be a positive integer.");
                        }
                        MaxResources = mr;
                        break;
                    case "JOBNAME":
                        if (string.IsNullOrWhiteSpace(opt.Value))
                        {
                            throw Error("Empty value for 'jobName' argument.");
                        }
                        Jobs.Add(new JobDefinition(opt.Value));
                        break;
                    case "JOBIDANDNAME":
                        if (string.IsNullOrWhiteSpace(opt.Value))
                        {
                            throw Error("Empty value for 'jobIdAndName' argument.");
                        }
                        int separatorIndex = opt.Value.IndexOf('#');
                        if (separatorIndex <= 0 || separatorIndex == opt.Value.Length - 1)
                        {
                            throw Error($"Invalid 'jobIdAndName' format '{opt.Value}'. Expected '<jobId>#<jobName>'.");
                        }
                        string jobId = opt.Value.Substring(0, separatorIndex);
                        string jobName = opt.Value.Substring(separatorIndex + 1);
                        Jobs.Add(new JobDefinition(jobName, jobId));
                        break;
                    case "CONFIGOUTPUTFILE":
                        ConfigOutputFile = opt.Value;
                        break;
                    case "CACHEENABLED":
                        CacheEnabled = true;
                        break;
                    case "ADOPROJECTID":
                        AdoProjectId = opt.Value;
                        break;
                    case "ADOCOLLECTIONURI":
                        AdoCollectionUri = opt.Value;
                        break;
                    case "ADOBUILDID":
                        AdoBuildId = opt.Value;
                        break;
                    case "ADOACCESSTOKENENVVAR":
                        AdoAccessTokenEnvVar = opt.Value;
                        break;
                    case "JOBID":
                        JobId = opt.Value;
                        break;
                    case "SESSIONCONFIGPATH":
                        SessionConfigPath = opt.Value;
                        break;
                    case "TESTFOLDER":
                        TestFolder = opt.Value;
                        break;
                    case "JOBEXECUTABLE":
                        JobExecutable = opt.Value;
                        break;
                    case "TESTEXECUTIONTYPE":
                        TestExecutionType = opt.Value;
                        break;
                    case "JOBARGUMENTS":
                        JobArguments = opt.Value;
                        break;
                    case "TESTPARSERTYPE":
                        TestParserType = opt.Value;
                        break;
                    case "JOBTIMEOUT":
                        JobTimeout = opt.Value;
                        break;
                    case "TESTCASETIMEOUT":
                        TestCaseTimeout = opt.Value;
                        break;
                    case "TESTDEPENDENCYHASH":
                        TestDependencyHashes.Add(opt.Value);
                        break;
                    case "PRIORITY":
                        if (!int.TryParse(opt.Value, out var p) || p < 0)
                        {
                            throw Error($"Invalid priority value '{opt.Value}'. Must be a non-negative integer.");
                        }
                        Priority = p;
                        break;
                    default:
                        throw Error($"Unsupported option: {opt.Name}");
                }
            }

            // Parse mode
            if (string.IsNullOrEmpty(mode))
            {
                throw Error("Missing mandatory argument 'mode'");
            }

            switch (mode.ToUpperInvariant())
            {
                case "CREATESESSION":
                    Mode = CloudTestMode.CreateSession;
                    break;
                case "UPDATEDYNAMICJOB":
                    Mode = CloudTestMode.UpdateDynamicJob;
                    break;
                case "CANCELSESSION":
                    Mode = CloudTestMode.CancelSession;
                    break;
                case "GENERATESESSIONCONFIG":
                    Mode = CloudTestMode.GenerateSessionConfig;
                    break;
                case "GENERATEUPDATEDYNAMICJOBCONFIG":
                    Mode = CloudTestMode.GenerateUpdateDynamicJobConfig;
                    break;
                case "WAITFORSESSIONCOMPLETION":
                    Mode = CloudTestMode.WaitForSessionCompletion;
                    break;
                default:
                    throw Error($"Invalid mode '{mode}'. Must be one of: createSession, updateDynamicJob, cancelSession, generateSessionConfig, generateUpdateDynamicJobConfig, waitForSessionCompletion");
            }

            Timeout = TimeSpan.FromMinutes(timeoutMinutes);

            // Validate args based on mode
            if (Mode == CloudTestMode.GenerateSessionConfig)
            {
                if (string.IsNullOrEmpty(ConfigOutputFile))
                {
                    throw Error("Missing mandatory argument 'configOutputFile' for mode 'generateSessionConfig'");
                }

                if (string.IsNullOrEmpty(BuildDropLocation))
                {
                    throw Error("Missing mandatory argument 'buildDropLocation' for mode 'generateSessionConfig'");
                }

                if (string.IsNullOrEmpty(Sku))
                {
                    throw Error("Missing mandatory argument 'sku' for mode 'generateSessionConfig'");
                }

                if (string.IsNullOrEmpty(Image))
                {
                    throw Error("Missing mandatory argument 'image' for mode 'generateSessionConfig'");
                }

                if (MaxResources <= 0)
                {
                    throw Error("Missing or invalid 'maxResources' for mode 'generateSessionConfig'");
                }

                if (Jobs.Count == 0)
                {
                    throw Error("At least one '/jobName' or '/jobIdAndName' argument is required for mode 'generateSessionConfig'");
                }

                if (string.IsNullOrEmpty(Tenant))
                {
                    throw Error("Missing mandatory argument 'tenant' for mode 'generateSessionConfig'");
                }

                // Validate ADO VstsContext args: if any is provided, all must be provided
                var adoFields = new[] { AdoProjectId, AdoCollectionUri, AdoBuildId, AdoAccessTokenEnvVar };
                bool anyAdo = Array.Exists(adoFields, f => !string.IsNullOrEmpty(f));
                bool allAdo = Array.TrueForAll(adoFields, f => !string.IsNullOrEmpty(f));
                if (anyAdo && !allAdo)
                {
                    throw Error("When specifying ADO context, all of 'adoProjectId', 'adoCollectionUri', 'adoBuildId', and 'adoAccessTokenEnvVar' must be provided.");
                }

                // Other common args (tokenEnvVar) are not required for generateSessionConfig
                return;
            }

            if (Mode == CloudTestMode.GenerateUpdateDynamicJobConfig)
            {
                if (string.IsNullOrEmpty(ConfigOutputFile))
                {
                    throw Error("Missing mandatory argument 'configOutputFile' for mode 'generateUpdateDynamicJobConfig'");
                }

                if (string.IsNullOrEmpty(Image))
                {
                    throw Error("Missing mandatory argument 'image' for mode 'generateUpdateDynamicJobConfig'");
                }

                if (string.IsNullOrEmpty(Sku))
                {
                    throw Error("Missing mandatory argument 'sku' for mode 'generateUpdateDynamicJobConfig'");
                }

                // Resolve session ID
                if (!string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(SessionIdFile))
                {
                    throw Error("Cannot specify both 'sessionId' and 'sessionIdFile'. Use one or the other.");
                }

                if (!string.IsNullOrEmpty(SessionIdFile))
                {
                    if (!System.IO.File.Exists(SessionIdFile))
                    {
                        throw Error($"Session ID file '{SessionIdFile}' does not exist.");
                    }

                    SessionId = System.IO.File.ReadAllText(SessionIdFile).Trim();
                }

                if (string.IsNullOrEmpty(SessionId))
                {
                    throw Error("Missing 'sessionId' or 'sessionIdFile' for mode 'generateUpdateDynamicJobConfig'");
                }

                // Job can be specified either by direct ID or by name + session config file
                if (string.IsNullOrEmpty(JobId) && (Jobs.Count == 0 || string.IsNullOrEmpty(SessionConfigPath)))
                {
                    throw Error("Either 'jobId' or both 'jobName' and 'sessionConfigPath' must be provided for mode 'generateUpdateDynamicJobConfig'");
                }

                if (!string.IsNullOrEmpty(JobId) && (!string.IsNullOrEmpty(SessionConfigPath) || Jobs.Count > 0))
                {
                    throw Error("Cannot specify 'jobId' together with 'jobName'/'sessionConfigPath'. Use one approach or the other.");
                }

                // Resolve job ID from session config file when using name-based lookup
                if (string.IsNullOrEmpty(JobId) && Jobs.Count > 0)
                {
                    if (Jobs.Count > 1)
                    {
                        throw Error("Only one 'jobName' can be specified for mode 'generateUpdateDynamicJobConfig'");
                    }

                    JobId = ResolveJobIdFromSessionConfig(Jobs[0].Name, SessionConfigPath);
                }

                if (string.IsNullOrEmpty(TestFolder))
                {
                    throw Error("Missing mandatory argument 'testFolder' for mode 'generateUpdateDynamicJobConfig'");
                }

                if (string.IsNullOrEmpty(JobExecutable))
                {
                    throw Error("Missing mandatory argument 'jobExecutable' for mode 'generateUpdateDynamicJobConfig'");
                }

                if (string.IsNullOrEmpty(TestExecutionType))
                {
                    throw Error("Missing mandatory argument 'testExecutionType' for mode 'generateUpdateDynamicJobConfig'");
                }

                return;
            }

            // Validate common required args (all other modes)
            if (string.IsNullOrEmpty(TokenEnvVar))
            {
                throw Error("Missing mandatory argument 'tokenEnvVar'");
            }

            

            if (string.IsNullOrEmpty(Tenant))
            {
                throw Error("Missing mandatory argument 'tenant'");
            }

            // Validate path arguments are well-formed
            ValidatePath(BodyFile, "bodyFile");
            ValidatePath(SessionIdFile, "sessionIdFile");
            ValidatePath(ConfigOutputFile, "configOutputFile");
            ValidatePath(SessionConfigPath, "sessionConfigPath");

                        // Validate mode-specific args
            if (Mode == CloudTestMode.CreateSession || Mode == CloudTestMode.UpdateDynamicJob)
            {
                if (string.IsNullOrEmpty(BodyFile))
                {
                    throw Error($"Missing mandatory argument 'bodyFile' for mode '{mode}'");
                }
            }

            if (Mode == CloudTestMode.UpdateDynamicJob || Mode == CloudTestMode.CancelSession || Mode == CloudTestMode.WaitForSessionCompletion)
            {
                // Ensure sessionId and sessionIdFile are not both specified
                if (!string.IsNullOrEmpty(SessionId) && !string.IsNullOrEmpty(SessionIdFile))
                {
                    throw Error("Cannot specify both 'sessionId' and 'sessionIdFile'. Use one or the other.");
                }

                // Resolve session ID from file if provided
                if (!string.IsNullOrEmpty(SessionIdFile))
                {
                    if (!System.IO.File.Exists(SessionIdFile))
                    {
                        throw Error($"Session ID file '{SessionIdFile}' does not exist.");
                    }

                    SessionId = System.IO.File.ReadAllText(SessionIdFile).Trim();
                }

                if (string.IsNullOrEmpty(SessionId))
                {
                    throw Error($"Missing 'sessionId' or 'sessionIdFile' for mode '{mode}'");
                }
            }

            if (Mode == CloudTestMode.CreateSession)
            {
                if (string.IsNullOrEmpty(SessionIdFile))
                {
                    throw Error($"Missing mandatory argument 'sessionIdFile' for mode '{mode}'");
                }
            }
        }

        /// <summary>
        /// Validates that a string represents a well-formed file path.
        /// </summary>
        private static void ValidatePath(string value, string argumentName)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            try
            {
                // Path.GetFullPath will throw on invalid characters or malformed paths
                System.IO.Path.GetFullPath(value);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException)
            {
                throw Error($"Argument '{argumentName}' has an invalid path: '{value}'. {ex.Message}");
            }
        }

        /// <summary>
        /// Reads a session config JSON file and finds the job ID for the given job name.
        /// </summary>
        private static string ResolveJobIdFromSessionConfig(string jobName, string sessionConfigPath)
        {
            if (!System.IO.File.Exists(sessionConfigPath))
            {
                throw new InvalidOperationException($"Session config file '{sessionConfigPath}' does not exist.");
            }

            string json = System.IO.File.ReadAllText(sessionConfigPath);

            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("dynamicGroupRequests", out var groups))
                {
                    foreach (var group in groups.EnumerateArray())
                    {
                        if (group.TryGetProperty("dynamicJobRequests", out var jobs))
                        {
                            foreach (var job in jobs.EnumerateArray())
                            {
                                if (job.TryGetProperty("jobName", out var nameElement)
                                    && string.Equals(nameElement.GetString(), jobName, StringComparison.Ordinal))
                                {
                                    if (job.TryGetProperty("jobId", out var idElement))
                                    {
                                        string id = idElement.GetString();
                                        if (!string.IsNullOrEmpty(id))
                                        {
                                            return id;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse session config file '{sessionConfigPath}': {ex.Message}");
            }

            throw new InvalidOperationException($"Job name '{jobName}' not found in session config file '{sessionConfigPath}'");
        }

    }
}
