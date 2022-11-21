// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Linq;
using System.Collections.Generic;

namespace BuildXL.Utilities.Authentication
{
    /// <summary>
    /// Possible types of PATs that are required for various BuildXL services.
    /// </summary>
    public enum PatType
    {
        /// <summary>
        /// Used for generic credential helpers.
        /// </summary>
        NotSpecified = 0,
        /// <summary>
        /// R/W permission for build cache.
        /// </summary>
        CacheReadWrite = 1,
        /// <summary>
        /// R/W permission for drop.
        /// </summary>
        VstsDropReadWrite = 2,
        /// <summary>
        /// R/W permission for symbols.
        /// </summary>
        SymbolsReadWrite = 3
    }

    /// <summary>
    /// Possible result types that can be returned during a credential acquisition operation.
    /// </summary>
    public enum CredentialHelperResultType
    {
        /// <summary>
        /// The credential provider environment variables were not set, or not found on disk.
        /// </summary>
        NoCredentialProviderSpecified = 0,

        /// <summary>
        /// Credential acquisiton was successful.
        /// </summary>
        Success = 1,

        /// <summary>
        /// The credential provider returned a bad status code.
        /// </summary>
        BadStatusCodeReturned = 2,

        /// <summary>
        /// An exception occured during execution of the credential provider.
        /// </summary>
        ExceptionOccurred = 3,

        /// <summary>
        /// Indicates that the process could not start or timed out.
        /// </summary>
        CredentialProviderRuntimeError = 4
    }

    /// <summary>
    /// The different types of credentials that could be returned by a credential helper.
    /// </summary>
    public enum CredentialType
    {
        /// <nodoc />
        PersonalAccessToken = 0,
        /// <nodoc />
        AadToken = 1,
    }

    /// <summary>
    /// Contains the result of a credential acquisition.
    /// </summary>
    public class CredentialHelperResult
    {
        /// <summary>
        /// Indicates whether credentials were acquired successfully.
        /// If <see cref="CredentialHelperResultType.NoCredentialProviderSpecified"/>, no credentials were acquired.
        /// If <see cref="CredentialHelperResultType.Success"/>, <see cref="CredentialHelperResult.Token"/> should be set with the token that was acquired.
        /// If <see cref="CredentialHelperResultType.BadStatusCodeReturned"/>, <see cref="CredentialHelperResultType.CredentialProviderRuntimeError"/>,
        /// or <see cref="CredentialHelperResultType.ExceptionOccurred"/>, the error will be logged to the verbose logger.
        /// </summary>
        public CredentialHelperResultType Result;

        /// <summary>
        /// The type of token returned by the credential helper.
        /// </summary>
        public CredentialType CredentialType;

        /// <summary>
        /// The token acquired from the credential helper.
        /// </summary>
        public string Token;

        /// <nodoc />
        public CredentialHelperResult()
        {
            Result = CredentialHelperResultType.NoCredentialProviderSpecified;
            Token = null;
        }
    }

    /// <summary>
    /// Represents data output from the credential provider
    /// </summary>
    internal struct GenericAuthOutput
    {
#pragma warning disable CS1591 // Missing XML comment
        public string Username { get; set; }
        public string Password { get; set; }
        public string Message { get; set; }
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// JSON output format for the Microsoft Authentication CLI
    /// CODESYNC: https://github.com/AzureAD/microsoft-authentication-cli/blob/main/docs/usage.md#output-formats
    /// </summary>
    internal struct AzureAuthOutput
    {
#pragma warning disable CS1591 // Missing XML comment
        [JsonProperty("user")]
        public string User { get; set; }
        [JsonProperty("display_name")]
        public string DisplayName { get; set; }
        [JsonProperty("token")]
        public string Token { get; set; }
        [JsonProperty("expiration_date")]
        public string ExpirationDate { get; set; }
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// Indicates what type of Credential helper is being used, which is then in turn used to determine what arguments need to be passed to the credential helper
    /// and how its output should be interpreted.
    /// </summary>
    public enum CredentialHelperType
    {
        /// <summary>
        /// Generic credential helper
        /// </summary>
        Generic = 0,
        /// <summary>
        /// Cloudbuild credential helper
        /// </summary>
        Cloudbuild = 1,
        /// <summary>
        /// Azure Auth CLI for MSAL auth through a credential provider instead of directly through BuildXL.
        /// This is useful for cases where BuildXL cannot properly support certain MSAL features such as the WAM broker.
        /// https://github.com/AzureAD/microsoft-authentication-cli
        /// </summary>
        MicrosoftAuthenticationCLI = 2
    }

    /// <summary>
    /// Helper class to retrieve credentials provided by a credential provider.
    /// </summary>
    /// <remarks>
    /// Any exceptions thrown by this due to invalid input should be handled by the caller.
    /// </remarks>
    public class CredentialProviderHelper
    {
        private readonly Dictionary<string, CredentialHelperType> m_credentialHelperToEnvironmentVariableMapping = new()
        {
            { GenericCredentialProvidersPathEnvVariable, CredentialHelperType.Generic },
            { CloudbuildCredentialHelperPathEnvVariable, CredentialHelperType.Cloudbuild },
            { AzureAuthCredentialProviderPathEnvVariable, CredentialHelperType.MicrosoftAuthenticationCLI },
        };

        private readonly CredentialHelperType m_credentialHelperType;

        /// <summary>
        /// This is the credential provider variable used by non-cloudbuild credential providers.
        /// </summary>
        private const string GenericCredentialProvidersPathEnvVariable = "ARTIFACT_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// This credential helper is only used in cloudbuild.
        /// </summary>
        private const string CloudbuildCredentialHelperPathEnvVariable = "AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// Support for AzureAuth CLI: https://github.com/AzureAD/microsoft-authentication-cli
        /// </summary>
        private const string AzureAuthCredentialProviderPathEnvVariable = "AZUREAUTH_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// Location of the credential helper executable.
        /// </summary>
        public readonly string CredentialHelperPath;

        /// <summary>
        /// Logger provided by caller to write log messages.
        /// </summary>
        private readonly Action<string> m_logger;

        /// <summary>
        /// Indicates that this instance of the object is being used for unit testing.
        /// </summary>
        private readonly bool m_createdForTesting = false;

        /// <summary>
        /// Arguments to be passed into to the credential helper executable during unit testing.
        /// </summary>
        private readonly string m_testingExeArguments = string.Empty;

        /// <summary>
        /// Create a <see cref="CredentialProviderHelper"/> object to provide ADO PATs.
        /// </summary>
        /// <param name="logger">Logging method to output debug information.</param>
        public CredentialProviderHelper(Action<string> logger)
        {
            m_logger = logger;

            foreach (var providerVariable in new string[] { CloudbuildCredentialHelperPathEnvVariable, GenericCredentialProvidersPathEnvVariable, AzureAuthCredentialProviderPathEnvVariable })
            {
                CredentialHelperPath = Environment.GetEnvironmentVariable(providerVariable);

                if (!string.IsNullOrWhiteSpace(CredentialHelperPath))
                {
                    Contract.Assert(m_credentialHelperToEnvironmentVariableMapping.TryGetValue(providerVariable, out m_credentialHelperType), $"The '{providerVariable}' environment variable is not mapped to a credential provider type.");

                    if (providerVariable != GenericCredentialProvidersPathEnvVariable)
                    {
                        // Valid credential provider exe found
                        break;
                    }
                    else
                    {
                        // For GenericCredentialProvidersPathEnvVariable, we must also detect the name of the exe
                        // We need to do this because the CloudbuildCredentialHelperPathEnvVariable variable which is set by Cloudbuild
                        // will always give us the exe name of the path instead of a search directory. 
                        // the GenericCredentialProvidersPathEnvVariable variable will give us only a directory, not an exe.
                        var providers = Directory.EnumerateFiles(CredentialHelperPath, "CredentialProvider*.exe", SearchOption.TopDirectoryOnly);

                        // BuildXL on Cloudbuild and ADO only use a single credential provider right now, so we can just pick the first one from the list
                        CredentialHelperPath = providers.Any() ? providers.First() : null;

                        break;
                    }
                }
            }

            // Verify that the credential helper path exists
            if (!string.IsNullOrWhiteSpace(CredentialHelperPath))
            {
                Contract.Assert(File.Exists(CredentialHelperPath), $"Credential helper specified at '{CredentialHelperPath}', but does not point to a valid path.");
            }
        }

        /// <summary>
        /// Constructor to be used for unit testing.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        /// <param name="credentialHelperType">The type of credential helper to test.</param>
        private CredentialProviderHelper(Action<string> logger, string credentialHelperPath, string arguments, CredentialHelperType credentialHelperType)
        {
            m_logger = logger;
            m_createdForTesting = true;
            m_testingExeArguments = arguments;
            CredentialHelperPath = credentialHelperPath;
            m_credentialHelperType = credentialHelperType;
        }

        /// <summary>
        /// Creates an instance of this class for unit testing with a specified executable location + arguments
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        /// <param name="credentialHelperType">The type of credential helper to test.</param>
        /// <returns></returns>
        public static CredentialProviderHelper CreateInstanceForTesting(Action<string> logger, string credentialHelperPath, string arguments, CredentialHelperType credentialHelperType)
        {
            return new CredentialProviderHelper(logger, credentialHelperPath, arguments, credentialHelperType);
        }

        /// <summary>
        /// Acquires a PAT based on the specified PAT type.
        /// </summary>
        /// <param name="uri">The Uri for which the caller is attempting to acquire a PAT.</param>
        /// <param name="type">The type of PAT the caller is requesting from the credential provider.</param>
        /// <returns></returns>
        public Task<CredentialHelperResult> AcquireTokenAsync(Uri uri, PatType type)
        {
            // Check whether a path was provided and that it exists
            if (!IsCredentialProviderSpecified())
            {
                // A credential helper was not specified or bad path was passed in the environment variable
                return Task.FromResult(new CredentialHelperResult
                {
                    Result = CredentialHelperResultType.NoCredentialProviderSpecified,
                });
            }

            return Task.Run(() => ExecuteHelperAndParseOutput(uri, type));
        }

        /// <summary>
        /// Tests whether the environment variable is set and the specified path exists.
        /// </summary>
        /// <returns>True if the credential provider is specified.</returns>
        public bool IsCredentialProviderSpecified()
        {
            return (!string.IsNullOrWhiteSpace(CredentialHelperPath) && File.Exists(CredentialHelperPath));
        }

        /// <summary>
        /// Executes the credential helper and returns the generated token.
        /// </summary>
        private CredentialHelperResult ExecuteHelperAndParseOutput(Uri uri, PatType authType)
        {
            var result = new CredentialHelperResult();
            var args = string.Empty;
            
            if (m_createdForTesting)
            {
                args = m_testingExeArguments;
            }
            else
            {
                switch (m_credentialHelperType)
                {
                    case CredentialHelperType.Generic:
                        args += $"-uri \"{uri}\"";
                        break;
                    case CredentialHelperType.Cloudbuild:
                        args += $"-uri \"{uri}\" -authMaterialFileName {authType}";
                        break;
                    case CredentialHelperType.MicrosoftAuthenticationCLI:
                        args += $"--client {VsoAadConstants.Client} --resource {VsoAadConstants.ResourceId} --tenant {VsoAadConstants.MicrosoftTenantId} --output json";
                        break;
                }
            }

            (var resultType, var stdOut) = ExecuteCredentialProvider(args);

            result.Result = resultType;

            if (resultType == CredentialHelperResultType.Success)
            {
                switch (m_credentialHelperType)
                {
                    case CredentialHelperType.Generic:
                    case CredentialHelperType.Cloudbuild:
                        var genericAuthOutput = JsonConvert.DeserializeObject<GenericAuthOutput>(stdOut);
                        result.CredentialType = CredentialType.PersonalAccessToken;
                        result.Token = genericAuthOutput.Password;
                        break;
                    case CredentialHelperType.MicrosoftAuthenticationCLI:
                        var azureAuthOutput = JsonConvert.DeserializeObject<AzureAuthOutput>(stdOut);
                        result.CredentialType = CredentialType.AadToken;
                        result.Token = azureAuthOutput.Token;
                        break;
                }
            }

            return result;
        }

        /// <summary>
        /// Executes the credential helper to acquire a token.
        /// </summary>
        private (CredentialHelperResultType result, string stdOut) ExecuteCredentialProvider(string args)
        {
            try
            {
                var stdOutBuilder = new StringBuilder();
                var stdErrBuilder = new StringBuilder();
                var arguments = string.Empty;

                var startInfo = new ProcessStartInfo
                {
                    FileName = CredentialHelperPath,
                    Arguments = args,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    ErrorDialog = false
                };
                var process = Process.Start(startInfo);
                var credentialProviderTimeout = TimeSpan.FromMinutes(5);

                if (process == null)
                {
                    // Process failed to start, return a null pat so that the caller falls back to an alternate credential provider
                    m_logger($"CredentialProviderHelper - Credential provider process was not able to start.");
                    return (CredentialHelperResultType.CredentialProviderRuntimeError, string.Empty);
                }

                process.OutputDataReceived += (o, e) => { stdOutBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (o, e) => { stdErrBuilder.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (CancellationToken.None.Register(() => KillProcess(process)))
                {
                    if (!process.WaitForExit((int)credentialProviderTimeout.TotalMilliseconds))
                    {
                        KillProcess(process);
                        m_logger($"CredentialProviderHelper - Credential provider took longer {credentialProviderTimeout.TotalSeconds} secs.");

                        return (CredentialHelperResultType.CredentialProviderRuntimeError, string.Empty); ;
                    }
                    // Give time for the Async event handlers to finish by calling WaitForExit again if the first one succeeded
                    // Note: Read remarks from https://msdn.microsoft.com/en-us/library/ty0d8k56(v=vs.110).aspx for reason.
                    process.WaitForExit();
                }

                process.CancelErrorRead();
                process.CancelOutputRead();

                if (process.ExitCode != 0)
                {
                    m_logger($"CredentialProviderHelper - Credential provider execution failed with exit code {process.ExitCode}.\n" +
                             $"StdOut: \n{stdOutBuilder}\nStdErr: \n{stdErrBuilder}\n");
                    return (CredentialHelperResultType.BadStatusCodeReturned, stdOutBuilder.ToString());
                }
                else
                {
                    m_logger($"CredentialProviderHelper - Credentials were successfully retrieved from provider.");
                    return (CredentialHelperResultType.Success, stdOutBuilder.ToString());
                }
            }
            catch (Exception ex)
            {
                m_logger($"CredentialProviderHelper - Exception occured during PAT acquisition. Exception Message:\n" + ex.ToString());
                return (CredentialHelperResultType.ExceptionOccurred, string.Empty);
            }
        }

        /// <summary>
        /// Tries to kill the specified process.
        /// </summary>
        /// <param name="process">Process to be killed.</param>
        private void KillProcess(Process process)
        {
            if (process.HasExited)
            {
                return;
            }

            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // the process may have exited,
                // in this case ignore the exception
            }
        }
    }
}
