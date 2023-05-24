// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Security;
using System.Threading.Tasks;
using BuildXL.Utilities.Core;
using Microsoft.Artifacts.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client.Broker;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;

namespace BuildXL.Utilities.Authentication
{
    /// <summary>
    /// This class creates a wrapper for ILogger to be used only by this authentication class.
    /// This is done because this class is used by various different parts of BuildXL, each implementing their own logger without using Microsoft.Extensions.Logging.
    /// </summary>
    internal class VssCredentialFactoryLogger : ILogger
    {
        private readonly Action<string> m_logger;

        /// <inheritdoc />
        public VssCredentialFactoryLogger(Action<string> logger)
        {
            m_logger = logger;
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
        {
            // Not used by VssCredentialsFactory
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            return true;
        }

        /// <inheritdoc />
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);
            m_logger($"[{logLevel}] {message}");
        }
    }

    /// <summary>
    /// Factory class for generating Vss credentials for build cache, drop, and symbol authentication.
    /// </summary>
    public class VssCredentialsFactory
    {
        private readonly SecureString m_pat;
        private readonly CredentialProviderHelper m_credentialHelper;
        private readonly VssCredentials m_credentials;
        private readonly ILogger m_logger;

        /// <summary>
        /// VssCredentialsFactory Constructor
        /// </summary>
        /// <param name="pat">A personal access token to use for authentication. Can be null.</param>
        /// <param name="helper">Credential provider helper class to be used if a credential provider is required for authentication. Can be null.</param>
        /// <param name="logger">Logger</param>
        public VssCredentialsFactory(string pat, CredentialProviderHelper helper, Action<string> logger)
            : this(ConvertStringToSecureString(pat), credentials: null, helper, logger) { }

        /// <summary>
        /// VssCredentialsFactory Constructor
        /// </summary>
        /// <param name="credentials">VSS credentials to be used. Can be null.</param>
        /// <param name="helper">Credential provider helper class to be used if a credential provider is required for authentication. Can be null.</param>
        /// <param name="logger">Logger</param>
        public VssCredentialsFactory(VssCredentials credentials, CredentialProviderHelper helper, Action<string> logger)
            : this(pat: null, credentials, helper, logger) { }

        /// <summary>
        /// VssCredentialsFactory Constructor
        /// </summary>
        /// <param name="pat">A personal access token to use for authentication. Can be null.</param>
        /// <param name="helper">Credential provider helper class to be used if a credential provider is required for authentication. Can be null.</param>
        /// <param name="logger">Logger</param>
        /// <param name="credentials">VSS credentials to be used. Can be null.</param>
        public VssCredentialsFactory(SecureString pat, VssCredentials credentials, CredentialProviderHelper helper, Action<string> logger)
        {
            m_credentialHelper = helper;
            m_logger = new VssCredentialFactoryLogger(logger);
            m_pat = pat;
            m_credentials = credentials;
        }

        /// <summary>
        /// Attempts to get VssCredentials in the following order, PAT authentication if provided, credential provider authentication if provided,
        /// then aad authentication.
        /// </summary>
        /// <param name="baseUri">Base URI for authentication request.</param>
        /// <param name="useAad">Whether AAD authentication should be done.</param>
        /// <param name="patType">
        /// Type of PAT to acquire if build is running on Cloudbuild. Use <see cref="PatType.NotSpecified"/> outside of Cloudbuild.
        /// </param>
        public async Task<VssCredentials> GetOrCreateVssCredentialsAsync(Uri baseUri, bool useAad, PatType patType)
        {
            Contract.Requires(baseUri != null);

            if (m_credentials != null)
            {
                return m_credentials;
            }

            // Credential helper should only be used on Windows CI machines (ie: inside Cloudbuild or ADO)
            // It can also be used as a backup if AAD authentication is not working, but AAD auth is 
            // generally easier to use and more reliable on local builds.
            // Only supported on Windows.
            if (m_credentialHelper != null && OperatingSystemHelper.IsWindowsOS)
            {
                var credentialHelperResult = await m_credentialHelper.AcquireTokenAsync(baseUri, patType);

                if (credentialHelperResult.Result == CredentialHelperResultType.Success)
                {
                    m_logger.LogInformation($"[VssCredentialsFactory] {credentialHelperResult.CredentialType} acquired from credential provider.");
                    
                    switch (credentialHelperResult.CredentialType)
                    {
                        case CredentialType.PersonalAccessToken:
                            return GetPatCredentials(ConvertStringToSecureString(credentialHelperResult.Token));
                        case CredentialType.AadToken:
                            return new VssAadCredential(new VssAadToken(VsoAadConstants.TokenType, credentialHelperResult.Token));
                        default:
                            m_logger.LogError($"[VssCredentialsFactory] Unsupported credential type returned by credential helper: {credentialHelperResult.CredentialType}.");
                            break;
                    }
                }
            }

            // PAT authentication without a credential helper should be used on non-windows CI machines.
            if (m_pat != null)
            {
                m_logger.LogInformation("[VssCredentialsFactory] PAT credentials used for authentication.");
                return GetPatCredentials(m_pat);
            }

            if (!useAad)
            {
                // If we have already gotten to this point, and useAad is not set, then there's no way to do authentication
                // Log this and return default vsscredentials. This will cause anything that requires authentication to fail later on.
                m_logger.LogError("[VssCredentialsFactory] Unable to acquire authentication token.");
                return new VssCredentials();
            }

            return await CreateVssCredentialsWithAadAsync(baseUri);
        }

        /// <summary>
        /// Performs AAD authentication with MSAL.
        /// </summary>
        public async Task<VssCredentials> CreateVssCredentialsWithAadAsync(Uri baseUri)
        {
            var app = AzureArtifacts.CreateDefaultBuilder(new Uri(VsoAadConstants.MicrosoftAuthority))
                .WithBroker(true, m_logger)
                .WithLogging((Microsoft.Identity.Client.LogLevel level, string message, bool containsPii) =>
                {
                    switch (level)
                    {
                        case Microsoft.Identity.Client.LogLevel.Error:
                            m_logger.LogError(message);
                            break;
                        case Microsoft.Identity.Client.LogLevel.Warning:
                            m_logger.LogWarning(message);
                            break;
                        default:
                            m_logger.LogInformation(message);
                            break;
                    }
                })
                .Build();

            var cache = await MsalCache.GetMsalCacheHelperAsync(MsalCache.DefaultMsalCacheLocation, m_logger);
            cache.RegisterCache(app.UserTokenCache);

            var providers = MsalTokenProviders.Get(app, m_logger);
            var tokenRequest = new TokenRequest(baseUri)
            {
                IsInteractive = true
            };

            Microsoft.Identity.Client.AuthenticationResult result = null;
            foreach (var provider in providers)
            {
                if (provider.CanGetToken(tokenRequest))
                {
                    try
                    {
                        result = await provider.GetTokenAsync(tokenRequest);
                        if (result != null)
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        m_logger.LogInformation($"Exception occured when acquiring token with token provider '{provider.Name}': '{e}'");
                    }
                }
            }

            return result != null
                ? new VssAadCredential(new VssAadToken(result.TokenType, result.AccessToken))
                : new VssAadCredential(new VssAadToken(VsoAadConstants.TokenType, string.Empty));
        }

        /// <summary>
        /// Converts a PAT secure string to a VssCredential.
        /// </summary>
        public static VssCredentials GetPatCredentials(SecureString pat)
        {
            return new VssBasicCredential(new NetworkCredential(string.Empty, pat));
        }

        /// <summary>
        /// Converts a string into a SecureString.
        /// </summary>
        public static SecureString ConvertStringToSecureString(string token)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                var secureStringToken = new SecureString();
                foreach (var c in token)
                {
                    secureStringToken.AppendChar(c);
                }

                return secureStringToken;
            }

            return null;
        }
    }
}
