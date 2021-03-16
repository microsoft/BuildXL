// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Possible types of PATs that are required for various BuildXL services.
    /// </summary>
    public enum PatType
    {
        /// <summary>
        /// R/W permission for build cache.
        /// </summary>
        CacheReadWrite = 0,
        /// <summary>
        /// R/W permission for drop.
        /// </summary>
        VstsDropReadWrite = 1,
        /// <summary>
        /// R/W permission for symbols.
        /// </summary>
        SymbolsReadWrite = 2
    }

    /// <summary>
    /// Possible result types that can be returned during a credential acquisition operation.
    /// </summary>
    public enum AzureArtifactsCredentialHelperResultType
    {
        /// <summary>
        /// AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH was not set or is invalid, this is a no-op status code.
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
        ExceptionOccured = 3,

        /// <summary>
        /// Indicates that the process could not start or timed out.
        /// </summary>
        CredentialProviderRuntimeError = 4
    }

    /// <summary>
    /// Contains the result of a credential acquisition.
    /// </summary>
    public class AzureArtifactsCredentialHelperResult
    {
        /// <summary>
        /// Indicates whether credentials were acquired successfully.
        /// If <see cref="AzureArtifactsCredentialHelperResultType.NoCredentialProviderSpecified"/>, no credentials were acquired.
        /// If <see cref="AzureArtifactsCredentialHelperResultType.Success"/>, <see cref="AzureArtifactsCredentialHelperResult.Pat"/> should be set with the PAT that was acquired.
        /// If <see cref="AzureArtifactsCredentialHelperResultType.BadStatusCodeReturned"/>, <see cref="AzureArtifactsCredentialHelperResultType.CredentialProviderRuntimeError"/>,
        /// or <see cref="AzureArtifactsCredentialHelperResultType.ExceptionOccured"/>, the error will be logged to the verbose logger.
        /// </summary>
        public AzureArtifactsCredentialHelperResultType Result;

        /// <summary>
        /// If success is true, then the PAT acquired will be set in this field.
        /// </summary>
        public SecureString Pat;

        /// <nodoc />
        public AzureArtifactsCredentialHelperResult()
        {
            Result = AzureArtifactsCredentialHelperResultType.NoCredentialProviderSpecified;
            Pat = null;
        }
    }

    /// <summary>
    /// Represents data output from the credential provider
    /// </summary>
    public struct AzureArtifactsAuthOutput
    {
#pragma warning disable CS1591 // Missing XML comment
        public string Username { get; set; }
        public string Password { get; set; }
        public string Message { get; set; }
#pragma warning restore CS1591 // Missing XML comment
    }


    /// <summary>
    /// Helper class to retrieve ADO credentials provided by a credential provider.
    /// </summary>
    /// <remarks>
    /// Any exceptions thrown by this due to invalid input should be handled by the caller.
    /// </remarks>
    public class AzureArtifactsCredentialHelper
    {
        /// <summary>
        /// Environment variable for the azure artifacts credential provider.
        /// </summary>
        private const string m_credentialHelperEnvironmentVariable = "AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// Location of the credential helper executable.
        /// </summary>
        public readonly string AzureArtifactsCredentialHelperPath;

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
        /// Create a <see cref="AzureArtifactsCredentialHelper"/> object to provide ADO PATs.
        /// </summary>
        /// <param name="logger">Logging method to output debug information.</param>
        public AzureArtifactsCredentialHelper(Action<string> logger)
        {
            m_logger = logger;
            AzureArtifactsCredentialHelperPath = Environment.GetEnvironmentVariable(m_credentialHelperEnvironmentVariable);
        }

        /// <summary>
        /// Constructor to be used for unit testing.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        private AzureArtifactsCredentialHelper(Action<string> logger, string credentialHelperPath, string arguments)
        {
            m_logger = logger;
            m_createdForTesting = true;
            m_testingExeArguments = arguments;
            AzureArtifactsCredentialHelperPath = credentialHelperPath;
        }

        /// <summary>
        /// Creates an instance of this class for unit testing with a specified executable location + arguments
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        /// <returns></returns>
        public static AzureArtifactsCredentialHelper CreateInstanceForTesting(Action<string> logger, string credentialHelperPath, string arguments)
        {
            return new AzureArtifactsCredentialHelper(logger, credentialHelperPath, arguments);
        }

        /// <summary>
        /// Acquires a PAT based on the specified PAT type using the credential provider set in the
        /// AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH environment variable.
        /// </summary>
        /// <param name="uri">The Uri for which the caller is attempting to acquire a PAT.</param>
        /// <param name="type">The type of PAT the caller is requesting from the credential provider.</param>
        /// <returns></returns>
        public Task<AzureArtifactsCredentialHelperResult> AcquirePat(Uri uri, PatType type)
        {
            // Check whether a path was provided and that it exists
            if (!IsAzureArtifactsCredentialProviderSpecified())
            {
                // Azure artifacts credential helper was not specified or bad path was passed in the environment variable
                // Fallback to using AAD or ARTIFACT_CREDENTIALPROVIDERS_PATH
                return Task.FromResult(new AzureArtifactsCredentialHelperResult
                {
                    Result = AzureArtifactsCredentialHelperResultType.NoCredentialProviderSpecified,
                });
            }

            return Task.Run(() => ExecuteParseAndOutputSecurePat(uri, type.ToString()));
        }

        /// <summary>
        /// Tests whether the environment variable is set and the specified path exists.
        /// </summary>
        /// <returns>True if the azure artifacts credential provider is specified.</returns>
        public bool IsAzureArtifactsCredentialProviderSpecified()
        {
            return (!string.IsNullOrWhiteSpace(AzureArtifactsCredentialHelperPath) && File.Exists(AzureArtifactsCredentialHelperPath));
        }

        /// <summary>
        /// Executes the credential helper and acquires a PAT.
        /// </summary>
        /// <param name="uri">The URI to provide to the credential provider</param>
        /// <param name="authType">The type of PAT that is being requested.</param>
        /// <returns><see cref="AzureArtifactsCredentialHelperResult"/> object with the result of the PAT acquisition operation.</returns>
        /// <remarks>If an error occured during PAT acquisition, it will be logged in the logger provided.</remarks>
        private AzureArtifactsCredentialHelperResult ExecuteParseAndOutputSecurePat(Uri uri, string authType)
        {
            var result = new AzureArtifactsCredentialHelperResult();

            try
            {
                var stdOut = new StringBuilder();
                var stdErr = new StringBuilder();
                var arguments = m_createdForTesting ? m_testingExeArguments : $"-uri \"{uri}\" -authMaterialFileName {authType}";
                var startInfo = new ProcessStartInfo
                {
                    FileName = AzureArtifactsCredentialHelperPath,
                    Arguments = arguments,
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
                    result.Result = AzureArtifactsCredentialHelperResultType.CredentialProviderRuntimeError;
                    m_logger($"AzureArtifactsCredentialHelper - Credential provider process was not able to start.");

                    return result;
                }

                process.OutputDataReceived += (o, e) => { stdOut.AppendLine(e.Data); };
                process.ErrorDataReceived += (o, e) => { stdErr.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                using (CancellationToken.None.Register(() => KillProcess(process)))
                {
                    if (!process.WaitForExit((int)credentialProviderTimeout.TotalMilliseconds))
                    {
                        KillProcess(process);
                        result.Result = AzureArtifactsCredentialHelperResultType.CredentialProviderRuntimeError;
                        m_logger($"AzureArtifactsCredentialHelper - Credential provider took longer {credentialProviderTimeout.TotalSeconds} secs.");

                        return result;
                    }
                    // Give time for the Async event handlers to finish by calling WaitForExit again if the first one succeeded
                    // Note: Read remarks from https://msdn.microsoft.com/en-us/library/ty0d8k56(v=vs.110).aspx for reason.
                    process.WaitForExit();
                }

                process.CancelErrorRead();
                process.CancelOutputRead();

                if (process.ExitCode != 0)
                {
                    result.Result = AzureArtifactsCredentialHelperResultType.BadStatusCodeReturned;
                    m_logger($"AzureArtifactsCredentialHelper - Credential provider execution failed with exit code {process.ExitCode}.\n" +
                             $"StdOut: \n{stdOut}\nStdErr: \n{stdErr}\n");
                }
                else
                {
                    result.Result = AzureArtifactsCredentialHelperResultType.Success;
                    result.Pat = DeserializeOutputAndGetPat(stdOut.ToString());
                    m_logger($"AzureArtifactsCredentialHelper - Credentials were successfully retrieved from provider.");
                }
            }
            catch (Exception ex)
            {
                result.Result = AzureArtifactsCredentialHelperResultType.ExceptionOccured;
                m_logger($"AzureArtifactsCredentialHelper - Exception occured during PAT acquisition. Exception Message:\n" + ex.ToString());
            }
            
            return result;
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

        /// <summary>
        /// Takes the output given by the credential provider and extracts the PAT.
        /// </summary>
        /// <param name="output">The std out from the credential provider.</param>
        /// <returns>The PAT acquired from the credential provider as a secure string.</returns>
        private SecureString DeserializeOutputAndGetPat(string output)
        {
            var deserialized = JsonConvert.DeserializeObject<AzureArtifactsAuthOutput>(output);
            var pat = new SecureString();

            foreach (char c in deserialized.Password)
            {
                pat.AppendChar(c);
            }

            if (m_createdForTesting)
            {
                // For testing purposes, emit the password in plaintext to be verified.
                m_logger(deserialized.Password);
            }

            return pat;
        }
    }
}
