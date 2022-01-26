// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Linq;
using System.Security;

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
    /// Contains the result of a credential acquisition.
    /// </summary>
    public class CredentialHelperResult
    {
        /// <summary>
        /// Indicates whether credentials were acquired successfully.
        /// If <see cref="CredentialHelperResultType.NoCredentialProviderSpecified"/>, no credentials were acquired.
        /// If <see cref="CredentialHelperResultType.Success"/>, <see cref="CredentialHelperResult.Pat"/> should be set with the PAT that was acquired.
        /// If <see cref="CredentialHelperResultType.BadStatusCodeReturned"/>, <see cref="CredentialHelperResultType.CredentialProviderRuntimeError"/>,
        /// or <see cref="CredentialHelperResultType.ExceptionOccurred"/>, the error will be logged to the verbose logger.
        /// </summary>
        public CredentialHelperResultType Result;

        /// <summary>
        /// If success is true, then the PAT acquired will be set in this field.
        /// </summary>
        public SecureString Pat;

        /// <nodoc />
        public CredentialHelperResult()
        {
            Result = CredentialHelperResultType.NoCredentialProviderSpecified;
            Pat = null;
        }
    }

    /// <summary>
    /// Represents data output from the credential provider
    /// </summary>
    internal struct AuthOutput
    {
#pragma warning disable CS1591 // Missing XML comment
        public string Username { get; set; }
        public string Password { get; set; }
        public string Message { get; set; }
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// Helper class to retrieve credentials provided by a credential provider.
    /// </summary>
    /// <remarks>
    /// Any exceptions thrown by this due to invalid input should be handled by the caller.
    /// </remarks>
    public class CredentialProviderHelper
    {
        /// <summary>
        /// This is the credential provider variable used by non-cloudbuild credential providers.
        /// </summary>
        private const string GenericCredentialProvidersPathEnvVariable = "ARTIFACT_CREDENTIALPROVIDERS_PATH";

        /// <summary>
        /// This credential helper is only used in cloudbuild.
        /// </summary>
        private const string CloudbuildCredentialHelperPathEnvVariable = "AZURE_ARTIFACTS_CREDENTIALPROVIDERS_PATH";

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

            // Default to cloudbuild credential provider
            CredentialHelperPath = Environment.GetEnvironmentVariable(CloudbuildCredentialHelperPathEnvVariable);
            if (string.IsNullOrWhiteSpace(CredentialHelperPath))
            {
                CredentialHelperPath = Environment.GetEnvironmentVariable(GenericCredentialProvidersPathEnvVariable);
                if (!string.IsNullOrWhiteSpace(CredentialHelperPath))
                {
                    // For GenericCredentialProvidersPathEnvVariable, we must also detect the name of the exe
                    // We need to do this because the CloudbuildCredentialHelperPathEnvVariable variable which is set by Cloudbuild
                    // will always give us the exe name of the path instead of a search directory. 
                    // the GenericCredentialProvidersPathEnvVariable variable will give us only a directory, not an exe.
                    var providers = Directory.EnumerateFiles(CredentialHelperPath, "CredentialProvider*.exe", SearchOption.TopDirectoryOnly);

                    // BuildXL on Cloudbuild and ADO only use a single credential provider right now, so we can just pick the first one from the list
                    CredentialHelperPath = providers.Any() ? providers.First() : null;
                }
            }
        }

        /// <summary>
        /// Constructor to be used for unit testing.
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        private CredentialProviderHelper(Action<string> logger, string credentialHelperPath, string arguments)
        {
            m_logger = logger;
            m_createdForTesting = true;
            m_testingExeArguments = arguments;
            CredentialHelperPath = credentialHelperPath;
        }

        /// <summary>
        /// Creates an instance of this class for unit testing with a specified executable location + arguments
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="credentialHelperPath"></param>
        /// <param name="arguments"></param>
        /// <returns></returns>
        public static CredentialProviderHelper CreateInstanceForTesting(Action<string> logger, string credentialHelperPath, string arguments)
        {
            return new CredentialProviderHelper(logger, credentialHelperPath, arguments);
        }

        /// <summary>
        /// Acquires a PAT based on the specified PAT type.
        /// </summary>
        /// <param name="uri">The Uri for which the caller is attempting to acquire a PAT.</param>
        /// <param name="type">The type of PAT the caller is requesting from the credential provider.</param>
        /// <returns></returns>
        public Task<CredentialHelperResult> AcquirePatAsync(Uri uri, PatType type)
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

            return Task.Run(() => ExecuteParseAndOutputSecurePat(uri, type));
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
        /// Executes the credential helper and acquires a PAT.
        /// </summary>
        /// <param name="uri">The URI to provide to the credential provider</param>
        /// <param name="authType">The type of PAT that is being requested.</param>
        /// <returns><see cref="CredentialHelperResult"/> object with the result of the PAT acquisition operation.</returns>
        /// <remarks>If an error occured during PAT acquisition, it will be logged in the logger provided.</remarks>
        private CredentialHelperResult ExecuteParseAndOutputSecurePat(Uri uri, PatType authType)
        {
            var result = new CredentialHelperResult();

            try
            {
                var stdOut = new StringBuilder();
                var stdErr = new StringBuilder();
                var arguments = string.Empty;
                if (m_createdForTesting)
                {
                    arguments = m_testingExeArguments;
                }
                else
                {
                    arguments = $"-uri \"{uri}\"";
                    if (authType != PatType.NotSpecified)
                    {
                        arguments += $" -authMaterialFileName {authType}";
                    }
                }
                var startInfo = new ProcessStartInfo
                {
                    FileName = CredentialHelperPath,
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
                    result.Result = CredentialHelperResultType.CredentialProviderRuntimeError;
                    m_logger($"CredentialProviderHelper - Credential provider process was not able to start.");

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
                        result.Result = CredentialHelperResultType.CredentialProviderRuntimeError;
                        m_logger($"CredentialProviderHelper - Credential provider took longer {credentialProviderTimeout.TotalSeconds} secs.");

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
                    result.Result = CredentialHelperResultType.BadStatusCodeReturned;
                    m_logger($"CredentialProviderHelper - Credential provider execution failed with exit code {process.ExitCode}.\n" +
                             $"StdOut: \n{stdOut}\nStdErr: \n{stdErr}\n");
                }
                else
                {
                    result.Result = CredentialHelperResultType.Success;
                    result.Pat = DeserializeOutputAndGetPat(stdOut.ToString());
                    m_logger($"CredentialProviderHelper - Credentials were successfully retrieved from provider.");
                }
            }
            catch (Exception ex)
            {
                result.Result = CredentialHelperResultType.ExceptionOccurred;
                m_logger($"CredentialProviderHelper - Exception occured during PAT acquisition. Exception Message:\n" + ex.ToString());
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
            var deserialized = JsonConvert.DeserializeObject<AuthOutput>(output);

            if (m_createdForTesting)
            {
                // For testing purposes, emit the password in plaintext to be verified.
                m_logger(deserialized.Password);
            }
            return ConvertStringPatToSecureStringPat(deserialized.Password);
        }

        /// <summary>
        /// Converts a string into a SecureString.
        /// </summary>
        public static SecureString ConvertStringPatToSecureStringPat(string pat)
        {
            if (!string.IsNullOrWhiteSpace(pat))
            {
                var secureStringPat = new SecureString();
                foreach (var c in pat)
                {
                    secureStringPat.AppendChar(c);
                }
                return secureStringPat;
            }
            return null;
        }
    }
}
