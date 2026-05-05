// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using BuildXL.ToolSupport;
using BuildXL.Utilities.Core;

namespace Tool.CloudTestClient
{
    /// <summary>
    /// CLI tool for interacting with the CloudTest Dynamic Job Execution (DJE) API.
    /// Supports creating sessions, updating dynamic jobs, cancelling sessions,
    /// and generating session configuration files.
    /// </summary>
    /// <remarks>
    /// See help text in PrintUsage() for detailed usage instructions and argument descriptions.
    /// </remarks>
    internal sealed class CloudTestClient : ToolProgram<CloudTestClientArgs>
    {
        private enum ReturnCode : int
        {
            Success = 0,
            HttpError = 1,
            Timeout = 2,
            SessionFailed = 3,
            InvalidInput = 4,
        }

        private static readonly TimeSpan s_readyPollInterval = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan s_completionPollInterval = TimeSpan.FromSeconds(20);

        /// <summary>
        /// CloudTest production API base URL.
        /// </summary>
        private const string BaseUrl = "https://api.prod.cloudtest.microsoft.com";

        private CloudTestClient() : base("CloudTestClient")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new CloudTestClient().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out CloudTestClientArgs arguments)
        {
            try
            {
                arguments = new CloudTestClientArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Console.Error.WriteLine();
                PrintUsage();
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(CloudTestClientArgs arguments)
        {
            return RunAsync(arguments).GetAwaiter().GetResult();
        }

        private async Task<int> RunAsync(CloudTestClientArgs arguments)
        {
            // GenerateSessionConfig and GenerateUpdateDynamicJobConfig are local-only operations — no HTTP or token needed
            if (arguments.Mode == CloudTestMode.GenerateSessionConfig)
            {
                return RunGenerateSessionConfig(arguments);
            }

            if (arguments.Mode == CloudTestMode.GenerateUpdateDynamicJobConfig)
            {
                return RunGenerateUpdateDynamicJobConfig(arguments);
            }

            var deadline = DateTime.UtcNow + arguments.Timeout;

            try
            {
                string token = Environment.GetEnvironmentVariable(arguments.TokenEnvVar);
                if (string.IsNullOrEmpty(token))
                {
                    Log($"ERROR: Environment variable '{arguments.TokenEnvVar}' is not set or empty.");
                    return (int)ReturnCode.InvalidInput;
                }

                Log($"Timeout set to {arguments.Timeout.TotalMinutes} minutes (deadline: {deadline:u})");

                using var httpClient = new HttpClient();
                httpClient.Timeout = arguments.Timeout;
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                switch (arguments.Mode)
                {
                    case CloudTestMode.CreateSession:
                        return await CreateSessionAsync(httpClient, arguments, deadline);

                    case CloudTestMode.UpdateDynamicJob:
                        return await UpdateDynamicJobAsync(httpClient, arguments);

                    case CloudTestMode.CancelSession:
                        return await CancelSessionAsync(httpClient, arguments);

                    case CloudTestMode.WaitForSessionCompletion:
                        return await WaitForSessionCompletionAsync(httpClient, arguments, deadline);

                    default:
                        Log($"ERROR: Unknown mode '{arguments.Mode}'.");
                        return (int)ReturnCode.InvalidInput;
                }
            }
            catch (Exception ex)
            {
                Log($"ERROR: Unhandled exception: {ex}");
                return (int)ReturnCode.HttpError;
            }
        }

        private async Task<int> CreateSessionAsync(HttpClient httpClient, CloudTestClientArgs arguments, DateTime deadline)
        {
            // Step 1: Read and submit the session
            string body = await File.ReadAllTextAsync(arguments.BodyFile);
            string submitUrl = $"{BaseUrl}/api/tenants/{arguments.Tenant}/sessions";

            Log($"Submitting session to {submitUrl}");
            var submitResponse = await PostAsync(httpClient, submitUrl, body);
            if (submitResponse == null)
            {
                return (int)ReturnCode.HttpError;
            }

            string sessionId = ExtractJsonField(submitResponse, "testSessionId");
            if (string.IsNullOrEmpty(sessionId))
            {
                Log($"ERROR: Could not extract 'testSessionId' from response: {submitResponse}");
                return (int)ReturnCode.HttpError;
            }

            // The CloudTest API may return HTTP 200 even when session creation failed.
            // Check the Result field to detect this case early instead of entering the polling loop.
            string submitResult = ExtractJsonField(submitResponse, "result");
            if (!string.IsNullOrEmpty(submitResult)
                && "Failed".Equals(submitResult, StringComparison.OrdinalIgnoreCase))
            {
                string exceptionData = ExtractJsonField(submitResponse, "exceptionData");
                Log($"ERROR: Session {sessionId} was created but immediately failed. Result={submitResult}");
                if (!string.IsNullOrEmpty(exceptionData))
                {
                    Log($"ExceptionData: {exceptionData}");
                }

                return (int)ReturnCode.SessionFailed;
            }

            Log($"Session created: {sessionId}");

            // Write session ID to file so downstream pips can consume it
            await File.WriteAllTextAsync(arguments.SessionIdFile, sessionId);
            Log($"Session ID written to {arguments.SessionIdFile}");

            // Step 2: Poll for session readiness
            string readyUrl = $"{BaseUrl}/api/tenants/{arguments.Tenant}/sessions/{sessionId}/Ready";
            Log($"Polling for session readiness at {readyUrl}");

            int readyResult = await PollAsync(
                httpClient,
                readyUrl,
                s_readyPollInterval,
                deadline,
                "Timeout reached while waiting for session readiness.",
                response =>
                {
                    string isReady = ExtractJsonField(response, "isReadyToAcceptDynamicJobs");
                    if ("true".Equals(isReady, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Session is ready to accept dynamic jobs.");
                        return (int)ReturnCode.Success;
                    }

                    Log("Session not ready yet, waiting...");
                    return null;
                });

            if (readyResult != (int)ReturnCode.Success)
            {
                return readyResult;
            }


            return (int)ReturnCode.Success;
        }


        private Task<int> WaitForSessionCompletionAsync(HttpClient httpClient, CloudTestClientArgs arguments, DateTime deadline)
        {
            string statusUrl = $"{BaseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}";
            Log($"Polling for session completion at {statusUrl}");

            return PollAsync(
                httpClient,
                statusUrl,
                s_completionPollInterval,
                deadline,
                "Timeout reached while waiting for session completion.",
                response =>
                {
                    string status = ExtractJsonField(response, "status");
                    Log($"Session status: {status}");

                    // Queued and Running are the only non-terminal states
                    if ("Queued".Equals(status, StringComparison.OrdinalIgnoreCase)
                        || "Running".Equals(status, StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    // Any other status is terminal (Completed, or unexpected)
                    string result = ExtractJsonField(response, "result");
                    Log($"Session completed with result: {result}");

                    if ("Passed".Equals(result, StringComparison.OrdinalIgnoreCase))
                    {
                        return (int)ReturnCode.Success;
                    }

                    // Non-Passed result: write the full response to stderr
                    Console.Error.WriteLine(response);
                    Log($"ERROR: Session result is '{result}' (expected 'Passed').");
                    return (int)ReturnCode.SessionFailed;
                });
        }

        private async Task<int> UpdateDynamicJobAsync(HttpClient httpClient, CloudTestClientArgs arguments)
        {
            string body = await File.ReadAllTextAsync(arguments.BodyFile);
            string url = $"{BaseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}/UpdateDynamicJob";

            Log($"Updating dynamic job at {url}");
            string response = await PostAsync(httpClient, url, body);
            if (response == null)
            {
                return (int)ReturnCode.HttpError;
            }

            Log($"Dynamic job updated successfully. Response: {response}");
            return (int)ReturnCode.Success;
        }

        private async Task<int> CancelSessionAsync(HttpClient httpClient, CloudTestClientArgs arguments)
        {
            string url = $"{BaseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}/cancel";

            Log($"Cancelling session at {url}");
            var response = await PostAsync(httpClient, url, content: null);
            if (response == null)
            {
                return (int)ReturnCode.HttpError;
            }

            Log($"Session cancelled successfully. Response: {response}");
            return (int)ReturnCode.Success;
        }

        private int RunGenerateSessionConfig(CloudTestClientArgs arguments)
        {
            try
            {
                string outputPath = ConfigGeneratorHelper.GenerateSessionConfig(arguments);
                Console.WriteLine($"Session configuration written to: {outputPath}");
                return (int)ReturnCode.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR generating session config: {ex.ToString()}");
                return (int)ReturnCode.InvalidInput;
            }
        }

        private int RunGenerateUpdateDynamicJobConfig(CloudTestClientArgs arguments)
        {
            try
            {
                string outputPath = ConfigGeneratorHelper.GenerateUpdateDynamicJobConfig(arguments);
                Console.WriteLine($"UpdateDynamicJob configuration written to: {outputPath}");
                return (int)ReturnCode.Success;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR generating UpdateDynamicJob config: {ex.ToString()}");
                return (int)ReturnCode.InvalidInput;
            }
        }


        /// <summary>
        /// Polls a URL until a terminal condition is met or the deadline expires.
        /// The evaluator returns null to keep polling, or a return code to stop.
        /// </summary>
        private static async Task<int> PollAsync(
            HttpClient httpClient,
            string url,
            TimeSpan pollInterval,
            DateTime deadline,
            string timeoutMessage,
            Func<string, int?> evaluateResponse)
        {
            TimeSpan remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                Log($"ERROR: {timeoutMessage}");
                return (int)ReturnCode.Timeout;
            }

            using var cts = new CancellationTokenSource(remaining);

            try
            {
                while (true)
                {
                    await Task.Delay(pollInterval, cts.Token);

                    string response = await GetAsync(httpClient, url, cts.Token);
                    if (response == null)
                    {
                        return (int)ReturnCode.HttpError;
                    }

                    int? result = evaluateResponse(response);
                    if (result.HasValue)
                    {
                        return result.Value;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Log($"ERROR: {timeoutMessage}");
                return (int)ReturnCode.Timeout;
            }
        }

        #region HTTP helpers

        private static async Task<string> PostAsync(HttpClient httpClient, string url, string content)
        {
            try
            {
                var httpContent = content != null
                    ? new StringContent(content, Encoding.UTF8, "application/json")
                    : null;

                var response = await httpClient.PostAsync(url, httpContent);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Log($"ERROR: POST {url} returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
                    return null;
                }

                return responseBody;
            }
            catch (HttpRequestException ex)
            {
                Log($"ERROR: POST {url} failed: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Log($"ERROR: POST {url} timed out.");
                return null;
            }
        }

        private static async Task<string> GetAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            try
            {
                var response = await httpClient.GetAsync(url, cancellationToken);
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    Log($"ERROR: GET {url} returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
                    return null;
                }

                return responseBody;
            }
            catch (HttpRequestException ex)
            {
                Log($"ERROR: GET {url} failed: {ex.Message}");
                return null;
            }
            catch (TaskCanceledException)
            {
                Log($"ERROR: GET {url} timed out.");
                return null;
            }
        }

        #endregion

        #region Utility

        private static string ExtractJsonField(string json, string fieldName)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (property.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        return property.Value.ToString();
                    }
                }
            }
            catch (JsonException)
            {
                // Fall through
            }

            return null;
        }

        private static void Log(string message)
        {
            string timestamped = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Console.WriteLine(timestamped);
        }

        #endregion

        private static void PrintUsage()
        {
            string helpText =
@"CloudTestClient - CLI tool for interacting with the CloudTest Dynamic Job Execution (DJE) API.

USAGE: CloudTestClient /mode:<mode> [options]

-- Common arguments (all modes) ---------------------------------------------------
  /mode              Required. One of: createSession, updateDynamicJob,
                     cancelSession, generateSessionConfig,
                     generateUpdateDynamicJobConfig, waitForSessionCompletion.
  /tenant            Required. CloudTest tenant name.
  /timeout           Optional. Overall timeout in minutes (default: 5).

-- createSession ------------------------------------------------------------------
  /bodyFile          Required. Path to JSON request body.
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Required. Output path to write the created session ID.

-- updateDynamicJob ---------------------------------------------------------------
  /bodyFile          Required. Path to JSON request body.
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Session ID from a file (e.g. produced by createSession).
  /sessionId         Explicit session ID GUID.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.

-- cancelSession ------------------------------------------------------------------
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Session ID from a file.
  /sessionId         Explicit session ID GUID.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.

-- generateSessionConfig ----------------------------------------------------------
  /configOutputFile  Required. Path to write the generated JSON.
  /buildDropLocation Required. Drop URL for the session.
  /tenant            Required. CloudTest tenant name.
  /sku               Required. VM SKU (e.g. Standard_D4s_v3).
  /image             Required. VM image (e.g. ubuntu22.04).
  /maxResources      Required. Number of parallel VMs.
  /jobName           Required (repeatable). Job name for a placeholder.
  /jobIdAndName      Optional (repeatable). Format: <jobId>#<jobName>.
  /displayName       Optional. Session display name.
  /user              Optional. Submitting user alias.
  /cacheEnabled      Optional. Enable job result caching (flag, default: false).
  /adoProjectId      Optional. Azure DevOps project ID (SYSTEM_TEAMPROJECTID).
  /adoCollectionUri  Optional. Azure DevOps collection URI (SYSTEM_COLLECTIONURI).
  /adoBuildId        Optional. Azure DevOps build ID (BUILD_BUILDID).
  /adoAccessTokenEnvVar Optional. Env var for ADO OAuth token.
  NOTE: If any ADO field is provided, all four must be provided.

-- generateUpdateDynamicJobConfig -------------------------------------------------
  /configOutputFile  Required. Path to write the generated JSON.
  /image             Required. VM image (e.g. ubuntu22.04).
  /sku               Required. VM SKU (e.g. Standard_D4s_v3).
  /sessionId         Session ID GUID.
  /sessionIdFile     Session ID from a file.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.
  /jobId             Job ID GUID (must match a declared placeholder).
  /jobName           Job name to look up in the session config file.
  /sessionConfigPath Path to session config JSON (for job name resolution).
  NOTE: Provide either /jobId alone, or both /jobName and /sessionConfigPath.
  /testFolder        Required. Relative path within the drop for test files.
  /jobExecutable     Required. Path to executable on the worker VM.
  /testExecutionType Required. One of: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest.
  /jobArguments      Optional. Arguments passed to the executable.
  /testParserType    Optional. One of: TRX, JUnit, TAEF, NUnitXml, TAP.
  /jobTimeout        Optional. Max job duration (HH:MM:SS).
  /testCaseTimeout   Optional. Per-test-case timeout (HH:MM:SS).
  /testDependencyHash Optional (repeatable). Hash of job inputs for caching.
  /priority          Optional. Job priority, lower = higher (default: 0).

-- waitForSessionCompletion -------------------------------------------------------
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Session ID from a file.
  /sessionId         Explicit session ID GUID.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.
  /timeout           Optional. Overall timeout in minutes (default: 5).

-- Return codes -------------------------------------------------------------------
  0 = Success
  1 = HTTP error
  2 = Timeout
  3 = Session failed (non-Passed result)
  4 = Invalid input
";
            Console.Error.WriteLine(helpText);
        }

    }
}
