// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// CloudTest API environment, determining the base URL for API calls.
    /// </summary>
    public enum CloudTestEnvironment
    {
        /// <summary>Production: https://api.cloudtest.microsoft.com</summary>
        Prod,
        /// <summary>Development: https://api.dev.cloudtest.microsoft.com</summary>
        Dev,
        /// <summary>Pre-production: https://api.ppe.cloudtest.microsoft.com</summary>
        PPE,
    }

    /// <summary>
    /// Represents a job definition referenced by name on the command line (via /jobName).
    /// </summary>
    public record JobDefinition(string Name);

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
        /// CloudTest API environment. Determines the base URL for API calls.
        /// Default is Prod.
        /// </summary>
        public CloudTestEnvironment Environment { get; } = CloudTestEnvironment.Prod;

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


        /// <summary>Path to the single JSON file describing the entire session (tenant, drop, groups, file providers, etc.).</summary>
        public string SessionInputFile { get; }

        /// <summary>VM SKU (e.g. Standard_D4s_v3). Used by generateUpdateDynamicJobConfig to compute the groupId when a job is referenced directly by ID.</summary>
        public string Sku { get; }

        /// <summary>VM image (e.g. ubuntu22.04). Used by generateUpdateDynamicJobConfig to compute the groupId when a job is referenced directly by ID.</summary>
        public string Image { get; }

        /// <summary>
        /// List of job definitions. For mode 'generateUpdateDynamicJobConfig' this holds the single job name to resolve
        /// to a job ID via the session config file.
        /// </summary>
        public List<JobDefinition> Jobs { get; } = new List<JobDefinition>();

        /// <summary>Path to write the generated session configuration JSON.</summary>
        public string ConfigOutputFile { get; }


        #endregion

        #region GenerateUpdateDynamicJobConfig arguments


        /// <summary>Specific job ID for the dynamic job update.</summary>
        public string JobId { get; private set; }

        /// <summary>
        /// Group ID for the dynamic job update. Resolved from the session config file when a job is referenced by name;
        /// otherwise computed from <see cref="Image"/> and <see cref="Sku"/>.
        /// </summary>
        public string GroupId { get; private set; }

        /// <summary>Path to the session config file for resolving a job name to a job ID.</summary>
        public string SessionConfigPath { get; }

        /// <summary>
        /// Optional group name used to disambiguate a job-name lookup when the job name is not unique across groups.
        /// Matches the group's name (defaults to "image sku") in the session config.
        /// </summary>
        public string GroupName { get; private set; }

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

        /// <summary>
        /// Drop-relative paths of artifacts contributing to the caching fingerprint (job inputs and
        /// session-creation drop artifacts). Aggregated into the fingerprint alongside the content hashes.
        /// </summary>
        public List<string> TestDependencyPaths { get; } = new List<string>();

        /// <summary>
        /// Raw JSON of the resolved group's dynamic setup and cleanup, captured from the session config when a job is
        /// referenced by name. Contributes to the job's caching fingerprint. Null when unavailable (e.g. direct ID).
        /// </summary>
        public string GroupSetupCleanupJson { get; private set; }

        /// <summary>
        /// Raw JSON of the session's file providers, captured from the session config when a job is referenced by
        /// name. Contributes to the job's caching fingerprint. Null when no file providers are configured.
        /// </summary>
        public string FileProvidersJson { get; private set; }

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
                    case "ENVIRONMENT":
                        switch (opt.Value?.ToUpperInvariant())
                        {
                            case "PROD":
                                Environment = CloudTestEnvironment.Prod;
                                break;
                            case "DEV":
                                Environment = CloudTestEnvironment.Dev;
                                break;
                            case "PPE":
                                Environment = CloudTestEnvironment.PPE;
                                break;
                            default:
                                throw Error($"Invalid environment value '{opt.Value}'. Must be one of: prod, dev, ppe.");
                        }
                        break;
                    case "SKU":
                        Sku = opt.Value;
                        break;
                    case "IMAGE":
                        Image = opt.Value;
                        break;
                    case "JOBNAME":
                        if (string.IsNullOrWhiteSpace(opt.Value))
                        {
                            throw Error("Empty value for 'jobName' argument.");
                        }
                        Jobs.Add(new JobDefinition(opt.Value));
                        break;
                    case "SESSIONINPUTFILE":
                        if (string.IsNullOrWhiteSpace(opt.Value))
                        {
                            throw Error("Empty value for 'sessionInputFile' argument.");
                        }
                        SessionInputFile = opt.Value;
                        break;
                    case "CONFIGOUTPUTFILE":
                        ConfigOutputFile = opt.Value;
                        break;
                    case "JOBID":
                        JobId = opt.Value;
                        break;
                    case "SESSIONCONFIGPATH":
                        SessionConfigPath = opt.Value;
                        break;
                    case "GROUPNAME":
                        GroupName = opt.Value;
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
                    case "TESTDEPENDENCYPATH":
                        TestDependencyPaths.Add(opt.Value);
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

                if (string.IsNullOrEmpty(SessionInputFile))
                {
                    throw Error("Missing mandatory argument 'sessionInputFile' for mode 'generateSessionConfig'");
                }

                // Note: do not return here. We still want common validations (e.g. path validation) to run.
            }

            if (Mode == CloudTestMode.GenerateUpdateDynamicJobConfig)
            {
                if (string.IsNullOrEmpty(ConfigOutputFile))
                {
                    throw Error("Missing mandatory argument 'configOutputFile' for mode 'generateUpdateDynamicJobConfig'");
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

                // Resolve job ID and group ID from session config file when using name-based lookup
                if (string.IsNullOrEmpty(JobId) && Jobs.Count > 0)
                {
                    if (Jobs.Count > 1)
                    {
                        throw Error("Only one 'jobName' can be specified for mode 'generateUpdateDynamicJobConfig'");
                    }

                    (JobId, GroupId, GroupSetupCleanupJson) = ResolveJobFromSessionConfig(Jobs[0].Name, GroupName, SessionConfigPath);

                    // The session's file providers are a session-level fingerprint input shared by every job.
                    FileProvidersJson = ReadFileProvidersJson(SessionConfigPath);
                }

                // When the group ID could not be resolved from the session config (i.e. a job referenced
                // directly by ID), it is computed from the image and sku, which then become mandatory.
                if (string.IsNullOrEmpty(GroupId))
                {
                    if (string.IsNullOrEmpty(Image))
                    {
                        throw Error("Missing mandatory argument 'image' for mode 'generateUpdateDynamicJobConfig' (required to compute the groupId when a job is referenced directly by ID)");
                    }

                    if (string.IsNullOrEmpty(Sku))
                    {
                        throw Error("Missing mandatory argument 'sku' for mode 'generateUpdateDynamicJobConfig' (required to compute the groupId when a job is referenced directly by ID)");
                    }
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

                // Note: do not return here. We still want common validations (e.g. path validation) to run.
            }

            // Validate common required args (all other modes)
            if (Mode != CloudTestMode.GenerateSessionConfig && Mode != CloudTestMode.GenerateUpdateDynamicJobConfig)
            {
                if (string.IsNullOrEmpty(TokenEnvVar))
                {
                    throw Error("Missing mandatory argument 'tokenEnvVar'");
                }

                if (string.IsNullOrEmpty(Tenant))
                {
                    throw Error("Missing mandatory argument 'tenant'");
                }
            }

            // Validate path arguments are well-formed
            ValidatePath(BodyFile, "bodyFile");
            ValidatePath(SessionIdFile, "sessionIdFile");
            ValidatePath(ConfigOutputFile, "configOutputFile");
            ValidatePath(SessionConfigPath, "sessionConfigPath");
            ValidatePath(SessionInputFile, "sessionInputFile");

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
        /// Reads a session config JSON file and finds the job ID and the containing group's ID for the given job name.
        /// Also captures the resolved group's dynamic setup/cleanup JSON so it can contribute to the job's caching
        /// fingerprint. When <paramref name="groupName"/> is provided, the lookup is restricted to the group with that
        /// name. When it is not provided and the job name matches jobs in more than one group, the lookup is ambiguous
        /// and fails.
        /// </summary>
        private static (string JobId, string GroupId, string GroupSetupCleanupJson) ResolveJobFromSessionConfig(string jobName, string groupName, string sessionConfigPath)
        {
            var matches = new List<(string JobId, string GroupId, string GroupName, string GroupSetupCleanupJson)>();

            try
            {
                using var doc = JsonHelpers.ReadJsonDocument(sessionConfigPath);

                if (doc.RootElement.TryGetProperty("dynamicGroupRequests", out var groups))
                {
                    foreach (var group in groups.EnumerateArray())
                    {
                        string currentGroupName = group.TryGetProperty("groupName", out var groupNameElement)
                            ? groupNameElement.GetString()
                            : null;

                        // When a group name filter is provided, only consider the matching group.
                        if (!string.IsNullOrEmpty(groupName) && !string.Equals(currentGroupName, groupName, StringComparison.Ordinal))
                        {
                            continue;
                        }

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
                                            // The session config generator always emits a groupId for every
                                            // group (SessionConfigGenerator.ComputeGroupId), so a found group
                                            // without one indicates a malformed or incompatible config. Fail
                                            // fast here rather than returning a null groupId that would later be
                                            // silently recomputed from the caller-supplied image/sku.
                                            string groupId = group.TryGetProperty("groupId", out var groupIdElement)
                                                ? groupIdElement.GetString()
                                                : null;
                                            if (string.IsNullOrEmpty(groupId))
                                            {
                                                throw new InvalidOperationException(
                                                    $"Group '{currentGroupName}' in session config file '{sessionConfigPath}' is missing a 'groupId'.");
                                            }
                                            matches.Add((id, groupId, currentGroupName, ExtractGroupSetupCleanupJson(group)));
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

            if (matches.Count == 0)
            {
                string scope = string.IsNullOrEmpty(groupName)
                    ? $"session config file '{sessionConfigPath}'"
                    : $"group '{groupName}' of session config file '{sessionConfigPath}'";
                throw new InvalidOperationException($"Job name '{jobName}' not found in {scope}");
            }

            if (matches.Count > 1)
            {
                string groupNames = string.Join(", ", matches.Select(m => $"'{m.GroupName}'"));
                throw new InvalidOperationException(
                    $"Job name '{jobName}' is ambiguous: it matches jobs in multiple groups ({groupNames}) in session config file '{sessionConfigPath}'. Specify a 'groupName' to disambiguate.");
            }

            return (matches[0].JobId, matches[0].GroupId, matches[0].GroupSetupCleanupJson);
        }

        /// <summary>
        /// Builds a stable JSON string capturing a group's dynamic setup and cleanup (the fingerprint-relevant
        /// group-level inputs). Always returns a non-null wrapper; an absent setup or cleanup is encoded as JSON null,
        /// so a group with neither still contributes a constant (and a group that later gains setup/cleanup changes it).
        /// </summary>
        private static string ExtractGroupSetupCleanupJson(System.Text.Json.JsonElement group)
        {
            string setup = group.TryGetProperty("dynamicGroupSetup", out var setupElement) ? setupElement.GetRawText() : null;
            string cleanup = group.TryGetProperty("dynamicGroupCleanup", out var cleanupElement) ? cleanupElement.GetRawText() : null;

            return $"{{\"dynamicGroupSetup\":{setup ?? "null"},\"dynamicGroupCleanup\":{cleanup ?? "null"}}}";
        }

        /// <summary>
        /// Reads the session-level file providers from a session config file and returns their raw JSON, or null when
        /// none are configured.
        /// </summary>
        private static string ReadFileProvidersJson(string sessionConfigPath)
        {
            try
            {
                using var doc = JsonHelpers.ReadJsonDocument(sessionConfigPath);
                if (doc.RootElement.TryGetProperty("fileProviders", out var fileProviders)
                    && fileProviders.ValueKind != System.Text.Json.JsonValueKind.Null)
                {
                    return fileProviders.GetRawText();
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse session config file '{sessionConfigPath}': {ex.Message}");
            }

            return null;
        }

    }
}
