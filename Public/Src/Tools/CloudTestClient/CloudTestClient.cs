// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
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
        /// <summary>
        /// Property names known to carry secrets (e.g. the VSTSContext holds the ADO auth token). Their values are
        /// replaced with "[REDACTED]" before a payload is written to the console.
        /// </summary>
        private static readonly HashSet<string> s_secretPayloadProperties = new(StringComparer.OrdinalIgnoreCase)
        {
            "VSTSContext",
        };
        
        private enum ReturnCode : int
        {
            Success = 0,
            HttpError = 1,
            Timeout = 2,
            SessionFailed = 3,
            InvalidInput = 4,
        }

        private static readonly TimeSpan s_readyPollInterval = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan s_completionPollInterval = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Maximum number of consecutive transient failures (e.g. an intermittent 502 from Azure Front Door,
        /// or a 429 while being throttled) tolerated while polling before giving up. A single success resets the counter.
        /// </summary>
        private const int MaxConsecutiveTransientPollFailures = 5;

        /// <summary>
        /// Upper bound on the exponential backoff delay applied after a transient poll failure (e.g. a 429/503).
        /// This backoff is added on top of the regular poll interval and doubles with each consecutive transient
        /// failure, so we ease off on a struggling or throttling service instead of hammering it at the normal cadence.
        /// </summary>
        internal static readonly TimeSpan MaxTransientBackoff = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Outcome of a single HTTP GET attempt, used to decide whether polling should stop, retry, or proceed.
        /// </summary>
        private enum HttpGetOutcome
        {
            /// <summary>The request succeeded; the body is available.</summary>
            Success,

            /// <summary>The request failed with a transient error (network hiccup, 5xx, throttling). Safe to retry.</summary>
            Transient,

            /// <summary>The request failed with a non-retryable error (e.g. auth redirect, 4xx). Polling should stop.</summary>
            Terminal,
        }

        /// <summary>
        /// HTTP status codes that indicate a transient, retryable condition. These are typically raised by
        /// intermediaries (e.g. Azure Front Door returning 502/503/504) or by server-side throttling, and
        /// usually clear on a subsequent attempt.
        /// </summary>
        private static readonly HashSet<HttpStatusCode> s_transientStatusCodes = new()
        {
            HttpStatusCode.RequestTimeout,        // 408
            HttpStatusCode.TooManyRequests,       // 429
            HttpStatusCode.InternalServerError,   // 500
            HttpStatusCode.BadGateway,            // 502
            HttpStatusCode.ServiceUnavailable,    // 503
            HttpStatusCode.GatewayTimeout,        // 504
        };

        /// <summary>
        /// Returns the CloudTest API base URL for the given environment.
        /// </summary>
        private static string GetBaseUrl(CloudTestEnvironment environment) => environment switch
        {
            CloudTestEnvironment.Prod => "https://api.prod.cloudtest.microsoft.com",
            CloudTestEnvironment.Dev => "https://api.dev.cloudtest.microsoft.com",
            CloudTestEnvironment.PPE => "https://api.ppe.cloudtest.microsoft.com",
            _ => throw new ArgumentOutOfRangeException(nameof(environment), environment, $"Unknown CloudTest environment '{environment}'."),
        };

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

        private static async Task<int> RunAsync(CloudTestClientArgs arguments)
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
                Log($"Environment: {arguments.Environment} ({GetBaseUrl(arguments.Environment)})");

                if (arguments.Debug)
                {
                    // Log the bearer token's non-sensitive claims (never the token itself) so audience/scope/expiry
                    // mismatches can be diagnosed. A request rejected with a 302 to sign-in despite a present token is
                    // typically caused by a wrong 'aud'/'scp' or an already-elapsed 'exp'.
                    LogTokenClaims(token);
                }

                // Do NOT follow redirects automatically. Protected CloudTest endpoints respond to an
                // unauthenticated/expired-token request with a 302 to the AAD sign-in page. If we followed it,
                // the final response would be a 200 carrying the sign-in HTML, which masks the auth failure and
                // gets mis-parsed as a session status. Keeping redirects off lets the 3xx surface through the
                // IsSuccessStatusCode checks so we log the status code and bail out early.
                using var httpClientHandler = new HttpClientHandler { AllowAutoRedirect = false };
                using var httpClient = new HttpClient(httpClientHandler);
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

        private static async Task<int> CreateSessionAsync(HttpClient httpClient, CloudTestClientArgs arguments, DateTime deadline)
        {
            // Step 1: Read and submit the session
            string body = await File.ReadAllTextAsync(arguments.BodyFile);
            string baseUrl = GetBaseUrl(arguments.Environment);
            string submitUrl = $"{baseUrl}/api/tenants/{arguments.Tenant}/sessions";

            Log($"Submitting session to {submitUrl}");
            var submitResponse = await PostAsync(httpClient, submitUrl, body, arguments.Debug);
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

            // Step 2: Kick off historic runtime retrieval concurrently with session readiness polling.
            // The Kusto query only needs job IDs (available from the body file), so it can run
            // in parallel with CloudTest provisioning the session.
            //
            // The retrieval is capped by the overall session-creation deadline: a slow (or hung) Kusto query
            // must never make us blow past the caller-provided timeout. We cancel at the deadline; on cancellation
            // the retrieval writes empty runtime files so downstream jobs can still proceed without historic data.
            using var historicRuntimesCts = new CancellationTokenSource();
            Task historicRuntimesTask = Task.CompletedTask;
            if (arguments.HistoricRuntimesEnabled)
            {
                TimeSpan remaining = deadline - DateTime.UtcNow;
                historicRuntimesCts.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
                historicRuntimesTask = RetrieveHistoricRuntimesAsync(arguments, historicRuntimesCts.Token);
            }

            // Step 3: Poll for session readiness
            string readyUrl = $"{baseUrl}/api/tenants/{arguments.Tenant}/sessions/{sessionId}/Ready";
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

            // Ensure historic runtime retrieval completes before returning
            await historicRuntimesTask;

            if (readyResult != (int)ReturnCode.Success)
            {
                return readyResult;
            }

            return (int)ReturnCode.Success;
        }


        private static Task<int> WaitForSessionCompletionAsync(HttpClient httpClient, CloudTestClientArgs arguments, DateTime deadline)
        {
            string baseUrl = GetBaseUrl(arguments.Environment);
            string statusUrl = $"{baseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}";
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

        private static async Task<int> UpdateDynamicJobAsync(HttpClient httpClient, CloudTestClientArgs arguments)
        {
            // Read and emit historic runtime info when a runtime file was provided for this job.
            if (!string.IsNullOrEmpty(arguments.HistoricRuntimeFile))
            {
                if (!File.Exists(arguments.HistoricRuntimeFile))
                {
                    Log($"ERROR: Historic runtime file '{arguments.HistoricRuntimeFile}' does not exist.");
                    return (int)ReturnCode.InvalidInput;
                }

                long? avgDurationMs = HistoricRuntimeHelper.ReadRuntimeFromFile(arguments.HistoricRuntimeFile);
                if (avgDurationMs.HasValue)
                {
                    Log($"Historic average runtime: {avgDurationMs.Value}ms");

                    if (avgDurationMs.Value >= 0)
                    {
                        // TODO: The exact way of sharing this with BuildXL is TBD, but the idea is to use a VSO-like approach
                        // where BuildXL will scan for ##buildxl lines in the standard output.
                        Console.WriteLine($"##buildxl[runtime]{avgDurationMs.Value}");
                    }
                    else
                    {
                        Log($"Historic average runtime is negative: {avgDurationMs.Value}ms. This is an indication there is no valid historic data.");
                    }
                }
                else
                {
                    Log($"WARNING: Historic runtime file is empty or does not contain 'avgDurationMs'. Proceeding without historic data.");
                }
            }

            string body = await File.ReadAllTextAsync(arguments.BodyFile);
            string baseUrl = GetBaseUrl(arguments.Environment);
            string url = $"{baseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}/UpdateDynamicJob";

            Log($"Updating dynamic job at {url}");
            string response = await PostAsync(httpClient, url, body, arguments.Debug);
            if (response == null)
            {
                return (int)ReturnCode.HttpError;
            }

            Log($"Dynamic job updated successfully. Response: {response}");
            return (int)ReturnCode.Success;
        }

        private static async Task<int> CancelSessionAsync(HttpClient httpClient, CloudTestClientArgs arguments)
        {
            string baseUrl = GetBaseUrl(arguments.Environment);
            string url = $"{baseUrl}/api/tenants/{arguments.Tenant}/sessions/{arguments.SessionId}/cancel";

            Log($"Cancelling session at {url}");
            var response = await PostAsync(httpClient, url, content: null, arguments.Debug);
            if (response == null)
            {
                return (int)ReturnCode.HttpError;
            }

            Log($"Session cancelled successfully. Response: {response}");
            return (int)ReturnCode.Success;
        }

        private static int RunGenerateSessionConfig(CloudTestClientArgs arguments)
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

        private static int RunGenerateUpdateDynamicJobConfig(CloudTestClientArgs arguments)
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
        /// Queries for historical average job runtimes and writes per-job runtime files.
        /// This is a best-effort operation: failures are logged but do not cause session creation to fail.
        /// Two authentication paths are supported:
        /// <list type="bullet">
        /// <item>Non-ADO: a pre-acquired Entra token is read from the environment variable named by
        /// <see cref="CloudTestClientArgs.HistoricRuntimeEntraTokenEnvVar"/>.</item>
        /// <item>ADO: workload identity federation — the client ID and tenant ID are extracted from the bearer
        /// token JWT, and a <see cref="KustoHistoricRuntimeHelper"/> is created using the service connection ID
        /// and the ADO system access token.</item>
        /// </list>
        /// </summary>
        private static async Task RetrieveHistoricRuntimesAsync(CloudTestClientArgs arguments, CancellationToken cancellationToken)
        {
            // The tool contract is that, once historic runtime retrieval is enabled, we must always
            // produce one file per job — even when retrieval fails. On any failure we write empty files, which the
            // reading side (UpdateDynamicJobAsync) treats as "no historic data".
            Dictionary<string, (string GroupName, string JobName)> jobIdToName;
            try
            {
                jobIdToName = HistoricRuntimeHelper.ExtractJobsFromSessionConfig(arguments.BodyFile);
            }
            catch (Exception ex)
            {
                Log($"WARNING: Failed to read jobs from session config for historic runtime retrieval: {ex}");
                return;
            }

            if (jobIdToName.Count == 0)
            {
                Log("WARNING: No jobs found in session config. No historic runtime files to write.");
                return;
            }

            try
            {
                KustoHistoricRuntimeHelper helper = CreateHistoricRuntimeHelper(arguments);
                await helper.RetrieveAndWriteRuntimesAsync(arguments.BodyFile, arguments.HistoricRuntimesOutputDir, arguments.Debug, Log, cancellationToken);
            }
            catch (Exception ex)
            {
                string reason = ex is OperationCanceledException
                    ? "the session-creation deadline was reached before the query completed"
                    : ex.ToString();
                Log($"WARNING: Failed to retrieve historic runtimes: {reason}. Writing empty runtime files so downstream jobs can proceed without historic data.");
                HistoricRuntimeHelper.WriteEmptyHistoricRuntimeFiles(arguments.HistoricRuntimesOutputDir, jobIdToName, Log);
            }
        }

        /// <summary>
        /// Creates a <see cref="KustoHistoricRuntimeHelper"/> configured for the current authentication mode.
        /// Throws when a required prerequisite (token, claim, or access token) is missing, so callers can write
        /// empty runtime files as a fallback.
        /// </summary>
        private static KustoHistoricRuntimeHelper CreateHistoricRuntimeHelper(CloudTestClientArgs arguments)
        {
            if (!string.IsNullOrEmpty(arguments.HistoricRuntimeEntraTokenEnvVar))
            {
                // Non-ADO case: use a pre-acquired Entra token from the specified environment variable.
                string entraToken = Environment.GetEnvironmentVariable(arguments.HistoricRuntimeEntraTokenEnvVar);
                if (string.IsNullOrEmpty(entraToken))
                {
                    throw new InvalidOperationException($"Environment variable '{arguments.HistoricRuntimeEntraTokenEnvVar}' is not set or empty.");
                }

                return new KustoHistoricRuntimeHelper(entraToken, arguments.Environment, arguments.Debug, Log);
            }

            // ADO case: use workload identity federation. The client ID and tenant ID are extracted from the
            // bearer token JWT, and authentication uses the service connection ID plus the ADO system access token.
            string token = Environment.GetEnvironmentVariable(arguments.TokenEnvVar);
            if (string.IsNullOrEmpty(token))
            {
                throw new InvalidOperationException($"Environment variable '{arguments.TokenEnvVar}' is not set or empty.");
            }

            if (!TryExtractJwtClaims(token, out string clientId, out string tenantId))
            {
                throw new InvalidOperationException("Failed to extract 'appid' and 'tid' claims from the bearer token.");
            }

            Log($"Extracted identity from token — tenantId: {tenantId}, clientId: {clientId}");

            string systemAccessToken = Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN");
            if (string.IsNullOrEmpty(systemAccessToken))
            {
                throw new InvalidOperationException("SYSTEM_ACCESSTOKEN environment variable is not set.");
            }

            return new KustoHistoricRuntimeHelper(tenantId, clientId, arguments.HistoricRuntimeServiceConnectionId, systemAccessToken, arguments.Environment, arguments.Debug, Log);
        }

        /// <summary>
        /// Extracts the 'appid' (client ID) and 'tid' (tenant ID) claims from a JWT bearer token.
        /// </summary>
        private static bool TryExtractJwtClaims(string token, out string clientId, out string tenantId)
        {
            clientId = null;
            tenantId = null;

            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            clientId = jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value;
            tenantId = jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;

            return !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(tenantId);
        }

        /// <summary>
        /// Logs the non-sensitive claims of the bearer token (never the token itself) to help diagnose
        /// authentication failures such as an audience/scope mismatch or an already-expired token.
        /// </summary>
        private static void LogTokenClaims(string token)
        {
            try
            {
                var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

                string audience = string.Join(", ", jwt.Audiences);
                string appId = jwt.Claims.FirstOrDefault(c => c.Type == "appid")?.Value;
                string tenantId = jwt.Claims.FirstOrDefault(c => c.Type == "tid")?.Value;
                string scopes = jwt.Claims.FirstOrDefault(c => c.Type == "scp")?.Value;
                string roles = string.Join(" ", jwt.Claims.Where(c => c.Type == "roles").Select(c => c.Value));

                Log($"Bearer token claims — aud: [{audience}], appid: {appId}, tid: {tenantId}, " +
                    $"scp: [{scopes}], roles: [{roles}], iat: {jwt.ValidFrom:u}, exp: {jwt.ValidTo:u} " +
                    $"(now: {DateTime.UtcNow:u}, {(jwt.ValidTo <= DateTime.UtcNow ? "EXPIRED" : "valid")}).");
            }
            catch (Exception ex)
            {
                Log($"WARNING: Could not decode bearer token to log claims: {ex}");
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

            int consecutiveTransientFailures = 0;

            try
            {
                while (true)
                {
                    await Task.Delay(pollInterval, cts.Token);

                    (HttpGetOutcome outcome, string response) = await GetAsync(httpClient, url, cts.Token);

                    if (outcome == HttpGetOutcome.Transient)
                    {
                        consecutiveTransientFailures++;
                        if (consecutiveTransientFailures > MaxConsecutiveTransientPollFailures)
                        {
                            Log($"ERROR: GET {url} failed with {consecutiveTransientFailures} consecutive transient errors. Giving up.");
                            return (int)ReturnCode.HttpError;
                        }

                        // Back off exponentially (on top of the regular poll interval) so we don't keep hammering a
                        // service that is throttling us (429) or temporarily unavailable (502/503/504).
                        TimeSpan backoff = ComputeTransientBackoff(pollInterval, consecutiveTransientFailures);
                        Log($"Transient failure while polling {url} ({consecutiveTransientFailures}/{MaxConsecutiveTransientPollFailures}); backing off {backoff.TotalSeconds:F0}s before retrying.");
                        await Task.Delay(backoff, cts.Token);
                        continue;
                    }

                    if (outcome == HttpGetOutcome.Terminal)
                    {
                        return (int)ReturnCode.HttpError;
                    }

                    // A successful response resets the transient failure streak.
                    consecutiveTransientFailures = 0;

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

        /// <summary>
        /// Computes an exponential backoff delay for a transient poll failure. The delay doubles with each
        /// consecutive failure — <paramref name="pollInterval"/> × 2^(consecutiveFailures-1) — and is capped at
        /// <see cref="MaxTransientBackoff"/>. It is applied in addition to the regular poll interval.
        /// </summary>
        internal static TimeSpan ComputeTransientBackoff(TimeSpan pollInterval, int consecutiveFailures)
        {
            // consecutiveFailures is always >= 1 here. Use double arithmetic to avoid ticks overflow on large exponents.
            double multiplier = Math.Pow(2, consecutiveFailures - 1);
            double backoffTicks = pollInterval.Ticks * multiplier;
            if (backoffTicks >= MaxTransientBackoff.Ticks)
            {
                return MaxTransientBackoff;
            }

            return TimeSpan.FromTicks((long)backoffTicks);
        }

        #region HTTP helpers

        private static async Task<string> PostAsync(HttpClient httpClient, string url, string content, bool debug)
        {
            try
            {
                var httpContent = content != null
                    ? new StringContent(content, Encoding.UTF8, "application/json")
                    : null;

                var response = await httpClient.PostAsync(url, httpContent);
                string responseBody = await response.Content.ReadAsStringAsync();

                // Log the submitted payload only after the request has been sent, so a serialization/redaction
                // problem can never prevent the actual submission.
                if (debug)
                {
                    LogRequest("POST", url, content);
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (IsRedirect(response.StatusCode))
                    {
                        Log($"ERROR: POST {url} was redirected ({(int)response.StatusCode} {response.StatusCode}) to '{response.Headers.Location}'. " +
                            "This usually indicates the bearer token is missing, invalid, or expired.");
                    }
                    else
                    {
                        Log($"ERROR: POST {url} returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
                    }

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

        private static async Task<(HttpGetOutcome Outcome, string Body)> GetAsync(HttpClient httpClient, string url, CancellationToken cancellationToken)
        {
            try
            {
                var response = await httpClient.GetAsync(url, cancellationToken);
                string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (IsRedirect(response.StatusCode))
                    {
                        Log($"ERROR: GET {url} was redirected ({(int)response.StatusCode} {response.StatusCode}) to '{response.Headers.Location}'. " +
                            "This usually indicates the bearer token is missing, invalid, or expired.");
                        return (HttpGetOutcome.Terminal, null);
                    }

                    if (s_transientStatusCodes.Contains(response.StatusCode))
                    {
                        Log($"WARNING: GET {url} returned transient error {(int)response.StatusCode} {response.StatusCode}.");
                        return (HttpGetOutcome.Transient, null);
                    }

                    Log($"ERROR: GET {url} returned {(int)response.StatusCode} {response.StatusCode}: {responseBody}");
                    return (HttpGetOutcome.Terminal, null);
                }

                return (HttpGetOutcome.Success, responseBody);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The overall polling deadline was reached. Surface the cancellation so the caller reports a timeout.
                throw;
            }
            catch (HttpRequestException ex)
            {
                // Network-level failures (connection reset, DNS, TLS, etc.) are typically transient.
                Log($"WARNING: GET {url} failed with a network error: {ex.Message}.");
                return (HttpGetOutcome.Transient, null);
            }
            catch (TaskCanceledException ex)
            {
                // Not the overall deadline (handled above): this is a per-request timeout, which is transient.
                Log($"WARNING: GET {url} timed out: {ex.Message}.");
                return (HttpGetOutcome.Transient, null);
            }
        }

        #endregion

        #region Utility

        private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode >= 300 && (int)statusCode < 400;

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

        /// <summary>
        /// Logs the details of an outgoing REST submission (HTTP method, URL, and JSON payload) to the console.
        /// Used when the /debug flag is enabled.
        /// </summary>
        private static void LogRequest(string method, string url, string content)
        {
            Log($"[DEBUG] {method} {url}");
            if (string.IsNullOrEmpty(content))
            {
                Log("[DEBUG] Request payload: <none>");
                return;
            }

            string formatted = FormatJson(content);
            if (formatted == null)
            {
                // The payload could not be parsed as JSON, so secrets could not be redacted from it. Skip printing it
                // entirely rather than risk leaking a secret.
                Log("[DEBUG] Request payload: <not valid JSON, omitted>");
                return;
            }

            Log($"[DEBUG] Request payload:{System.Environment.NewLine}{formatted}");
        }

        /// <summary>
        /// Pretty-prints a JSON string for readable debug logging, replacing the value of any property that carries a
        /// secret (see <see cref="s_secretPayloadProperties"/>) with "[REDACTED]". Returns null when the input is not
        /// valid JSON, in which case the payload must not be printed since secrets could not be redacted from it.
        /// </summary>
        private static string FormatJson(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                using var stream = new MemoryStream();
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    WriteRedacted(doc.RootElement, writer);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Recursively copies a JSON element to the writer, replacing the value of any object property whose name is
        /// in <see cref="s_secretPayloadProperties"/> with the string "[REDACTED]".
        /// </summary>
        private static void WriteRedacted(JsonElement element, Utf8JsonWriter writer)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    writer.WriteStartObject();
                    foreach (var property in element.EnumerateObject())
                    {
                        writer.WritePropertyName(property.Name);
                        if (s_secretPayloadProperties.Contains(property.Name))
                        {
                            writer.WriteStringValue("[REDACTED]");
                        }
                        else
                        {
                            WriteRedacted(property.Value, writer);
                        }
                    }
                    writer.WriteEndObject();
                    break;
                case JsonValueKind.Array:
                    writer.WriteStartArray();
                    foreach (var item in element.EnumerateArray())
                    {
                        WriteRedacted(item, writer);
                    }
                    writer.WriteEndArray();
                    break;
                default:
                    element.WriteTo(writer);
                    break;
            }
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
  /tenant            Required for all modes except generateUpdateDynamicJobConfig.
                     CloudTest tenant name.
  /timeout           Optional. Overall timeout in minutes (default: 5).
  /debug             Optional. When present, logs the JSON payloads of all REST
                     submissions performed against the CloudTest endpoint to the
                     console.
  /environment       Optional. CloudTest API environment: prod, dev, ppe
                     (default: prod).
                       prod -> [https://api.prod.cloudtest.microsoft.com](https://api.prod.cloudtest.microsoft.com)
                       dev  -> [https://api.dev.cloudtest.microsoft.com](https://api.dev.cloudtest.microsoft.com)
                       ppe  -> [https://api.ppe.cloudtest.microsoft.com](https://api.ppe.cloudtest.microsoft.com)

-- createSession ------------------------------------------------------------------
  /bodyFile          Required. Path to JSON request body.
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Required. Output path to write the created session ID.
  Historic runtime retrieval (optional; enabled by providing an identity able
  to read CloudTest telemetry):
  /historicRuntimeServiceConnectionId Azure DevOps service connection ID for
                     workload identity federation (ADO). Mutually exclusive with
                     /historicRuntimeEntraTokenEnvVar.
  /historicRuntimeEntraTokenEnvVar Env var holding an Entra token authorized to
                     read the CloudTest database (non-ADO). Mutually exclusive
                     with /historicRuntimeServiceConnectionId.
  /historicRuntimesOutputDir Directory to write per-job historic runtime JSON
                     files (one file per job ID). Required when an identity is
                     provided.

-- updateDynamicJob ---------------------------------------------------------------
  /bodyFile          Required. Path to JSON request body.
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Session ID from a file (e.g. produced by createSession).
  /sessionId         Explicit session ID GUID.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.
  /historicRuntimeFile Optional. Path to a JSON file with historic runtime
                     data for this job. When present, emits ##buildxl[runtime].

-- cancelSession ------------------------------------------------------------------
  /tokenEnvVar       Required. Env var holding the bearer token.
  /sessionIdFile     Session ID from a file.
  /sessionId         Explicit session ID GUID.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.

-- generateSessionConfig ----------------------------------------------------------
  /configOutputFile  Required. Path to write the generated session config JSON.
  /sessionInputFile  Required. Path to a single JSON file describing the entire
                     session. Top-level fields: tenant, buildDropLocation,
                     displayName, user, cacheEnabled, stamp, properties
                     ([{key,value}] pairs), featureExceptions (string array),
                     ado ({projectId, collectionUri, buildId, accessTokenEnvVar}),
                     groups (array; each with sku, image, maxResources, optional
                     maxParallelismForJobs, optional name, jobs, optional
                     dynamic setup/cleanup, and optional legacyModuleIdConfigPath),
                     and fileProviders. tenant,
                     buildDropLocation, and at least one group are required.

-- generateUpdateDynamicJobConfig -------------------------------------------------
  /configOutputFile  Required. Path to write the generated JSON.
  /sessionId         Session ID GUID.
  /sessionIdFile     Session ID from a file.
  NOTE: Provide exactly one of /sessionIdFile or /sessionId.
  /jobId             Job ID GUID (must match a declared placeholder).
  /jobName           Job name to look up in the session config file.
  /sessionConfigPath Path to session config JSON (for job name resolution).
  /groupName         Group name identifying which group to scope the /jobName
                     lookup to. Required with /jobName.
  NOTE: Provide either /jobId alone, or both /jobName and /sessionConfigPath.
  /image             Required only with /jobId. VM image (e.g. ubuntu22.04);
                     used to compute the groupId when referencing a job by ID.
  /sku               Required only with /jobId. VM SKU (e.g. Standard_D4s_v3);
                     used to compute the groupId when referencing a job by ID.
  /testFolder        Required. Relative path within the drop for test files.
  /jobExecutable     Required. Path to executable on the worker VM.
  /testExecutionType Required. One of: MsTest, Exe, TAEF, NUnit, XUnit, BoostTest.
  /jobArguments      Optional. Arguments passed to the executable.
  /testParserType    Optional. One of: TRX, JUnit, TAEF, NUnitXml, TAP.
  /jobTimeout        Optional. Max job duration (HH:MM:SS).
  /testCaseTimeout   Optional. Per-test-case timeout (HH:MM:SS).
  /testDependencyHash Optional (repeatable). VsoHash of a job input or session
                     drop artifact, folded into the caching fingerprint.
  /testDependencyPath Optional (repeatable). Drop-relative path paired with the
                     /testDependencyHash at the same position. Provide one path
                     per hash, in matching order.
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
