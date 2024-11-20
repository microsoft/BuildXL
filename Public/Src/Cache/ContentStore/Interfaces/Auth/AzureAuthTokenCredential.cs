// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// Interacts with the azure-auth-helper tool (see https://github.com/microsoft/ado-codespaces-auth) in order to obtain a bearer token
    /// </summary>
    /// <remarks>
    /// Assumes the azure auth helper tool is installed in a given path
    /// </remarks>
    public class AzureAuthTokenCredential : TokenCredential
    {
        private readonly string _azureAuthHelperPath;

        /// <summary>
        /// The name of the auth helper tool
        /// </summary>
        public const string AuthHelperToolName = "azure-auth-helper";

        /// <nodoc/>
        public AzureAuthTokenCredential(string azureAuthHelperPath)
        {
            Contract.Requires(azureAuthHelperPath != null);
            _azureAuthHelperPath = azureAuthHelperPath;
        }

        /// <summary>
        /// Tries to find the azure-auth-helper tool in PATH.
        /// </summary>
        /// <returns>The full path to the tool or null if the tool is not found</returns>
        /// <param name="failure">The exception message, if any exceptions occurred during the lookup</param>
        /// <remarks>The presence of the tool can be used as an indication of whether this authentication method is available</remarks>
        public static string? FindAuthHelperTool(out string? failure)
            => FindAuthHelperToolInternal("PATH", out failure);

        /// <summary>
        /// For testing purposes, allows to specify the environment variable to use as PATH
        /// </summary>
        internal static string? FindAuthHelperToolForTesting(string environmentVariableName, out string? failure)
            => FindAuthHelperToolInternal(environmentVariableName, out failure);

        private static string? FindAuthHelperToolInternal(string environmentVariableName, out string? failure)
        {
            failure = null;
            try
            {
                var directory = Environment
                    .GetEnvironmentVariable(environmentVariableName)
                    ?.Split(Path.PathSeparator)
                    .FirstOrDefault(path => File.Exists(Path.Combine(path, AuthHelperToolName)));

                if (directory == null)
                {
                    return null;
                }

                return Path.Combine(directory, AuthHelperToolName);
            }
            catch (Exception ex)
            {
                // If there is any problem, just record the failure for logging purposes and claim that it was not found
#pragma warning disable EPC12 // Suspicious exception handling: only the 'Message' property is observed in the catch block
                failure = ex.Message;
#pragma warning restore EPC12 // Suspicious exception handling: only the 'Message' property is observed in the catch block
                return null;
            }
        }

        /// <inheritdoc/>
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return GetTokenAsync(requestContext, cancellationToken).AsTask().GetAwaiter().GetResult();
        }

        /// <inheritdoc/>
        public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            // Calling the tool with 'get-access-token' should make the tool return (when it succeeds) the bearer token as part of standard output.
            // The expected cli is 'azure-auth-helper get-access-token <scopes>'
            // We shouldn't make any attempt to cache this. That's the caller responsibility.
            var scopes = requestContext.Scopes == null 
                ? string.Empty
                : string.Join(" ", requestContext.Scopes);

            ProcessStartInfo processStartInfo = new ProcessStartInfo(_azureAuthHelperPath, $"get-access-token {scopes}") {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false};

            var process = Process.Start(processStartInfo);

            if (process == null)
            {
                throw new InvalidOperationException($"Cannot start process '{_azureAuthHelperPath}'.");
            }

#if NETCOREAPP
            await process.WaitForExitAsync(cancellationToken);
#else
            process.WaitForExit();
#endif

            if (process.ExitCode != 0 )
            {
                throw new InvalidOperationException($"Executing '${_azureAuthHelperPath}' returned with exit code '{process.ExitCode}'. Details: {await process.StandardError.ReadToEndAsync()}");
            }

            // The bearer token should be represented by the full standard output. Just make sure newline characters are not present.
            var token = (await process.StandardOutput.ReadToEndAsync()).TrimEnd('\r', '\n');

            // The expiration time is for now not exposed by the auth tool. The minimum is 20 minutes, so let's use that to stay safe. Rationale:
            // * The token can have a life anywhere from 60-90 minutes: https://learn.microsoft.com/en-us/entra/identity-platform/configurable-token-lifetimes#access-tokens
            // * The authentication provider gives a refreshed token when the remaining time for the current access token < 2/3 * Max token life (60 minutes):
            // https://github.com/microsoft/vscode/blob/ba9f3299846123843ebb632d2bc89985c590930b/extensions/microsoft-authentication/src/AADHelper.ts#L88C2-L89C1
            return new AccessToken(token, DateTimeOffset.UtcNow + TimeSpan.FromMinutes(20));
        }
    }
}
