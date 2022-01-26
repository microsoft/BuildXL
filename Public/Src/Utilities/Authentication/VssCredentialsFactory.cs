// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Authentication
{
    /// <summary>
    /// Factory class for generating Vss credentials for build cache, drop, and symbol authentication.
    /// </summary>
    public class VssCredentialsFactory
    {
        private readonly SecureString m_pat;
        private readonly CredentialProviderHelper m_credentialHelper;
        private readonly VssCredentials m_credentials;
        private readonly Action<string> m_logger;
        private readonly string m_tokenCacheDirectory;
        private readonly string m_tokenCacheFileName = "buildxl_msalcache";

        /// <summary>
        /// VssCredentialsFactory Constructor
        /// </summary>
        /// <param name="pat">A personal access token to use for authentication. Can be null.</param>
        /// <param name="helper">Credential provider helper class to be used if a credential provider is required for authentication. Can be null.</param>
        /// <param name="logger">Logger</param>
        public VssCredentialsFactory(string pat, CredentialProviderHelper helper, Action<string> logger)
            : this(CredentialProviderHelper.ConvertStringPatToSecureStringPat(pat), credentials: null, helper, logger) { }

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
            m_logger = logger;
            m_pat = pat;
            m_credentials = credentials;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            m_tokenCacheDirectory = OperatingSystemHelper.IsWindowsOS
                ? Path.Combine(userProfile, "AppData", "Local", "BuildXL", "MsalTokenCache")
                : Path.Combine(userProfile, ".BuildXL", "MsalTokenCache");
            if (!Directory.Exists(m_tokenCacheDirectory))
            {
                Directory.CreateDirectory(m_tokenCacheDirectory);
            }
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
                var credentialHelperResult = await m_credentialHelper.AcquirePatAsync(baseUri, patType);

                if (credentialHelperResult.Result == CredentialHelperResultType.Success)
                {
                    m_logger("[VssCredentialsFactory] PAT acquired from credential provider.");
                    return GetPatCredentials(credentialHelperResult.Pat);
                }
            }

            // PAT authentication without a credential helper should be used on non-windows CI machines.
            if (m_pat != null)
            {
                m_logger("[VssCredentialsFactory] PAT credentials used for authentication.");
                return GetPatCredentials(m_pat);
            }

            if (!useAad)
            {
                // If we have already gotten to this point, and useAad is not set, then there's no way to do authentication
                // Log this and return default vsscredentials. This will cause anything that requires authentication to fail later on.
                m_logger("[VssCredentialsFactory] Unable to acquire authentication token.");
                return new VssCredentials();
            }

            return await CreateVssCredentialsWithAadAsync();
        }

        /// <summary>
        /// Calls MSAL to get authentication token with AAD.
        /// </summary>
        /// <returns></returns>
        /// <remarks>https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/Acquiring-tokens-interactively</remarks>
        public async Task<VssCredentials> CreateVssCredentialsWithAadAsync()
        {
            // 1. Configuration
            var app = PublicClientApplicationBuilder
                .Create(VsoAadConstants.Client)
                .WithTenantId(VsoAadConstants.MicrosoftTenantId)
                .WithRedirectUri(VsoAadConstants.RedirectUri)
                .Build();

            // 2. Token cache
            // https://docs.microsoft.com/en-us/azure/active-directory/develop/msal-net-token-cache-serialization?tabs=desktop
            // https://github.com/AzureAD/microsoft-authentication-extensions-for-dotnet/wiki/Cross-platform-Token-Cache
            var storageProperties = new StorageCreationPropertiesBuilder(m_tokenCacheFileName, m_tokenCacheDirectory).Build();
            var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
            cacheHelper.RegisterCache(app.UserTokenCache);

            // 3. Try silent authentication
            var accounts = await app.GetAccountsAsync();
            var scopes = new string[] { VsoAadConstants.Scope };
            AuthenticationResult result = null;

            try
            {
                result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                    .ExecuteAsync();
                m_logger("[VssCredentialsFactory] Successfully acquired authentication token through silent AAD authentication.");
            }
            catch (MsalUiRequiredException)
            {
                // 4. Interactive Authentication
                m_logger("[VssCredentialsFactory] Unable to acquire authentication token through silent AAD authentication.");
                // On Windows, we can try Integrated Windows Authentication which will fallback to interactive auth if that fails
                result = OperatingSystemHelper.IsWindowsOS 
                    ? await CreateVssCredentialsWithAadForWindowsAsync(app, scopes)
                    : await CreateVssCredentialsWithAadInteractiveAsync(app, scopes);
            }
            catch (Exception ex)
            {
                m_logger($"[VssCredentialsFactory] Unable to acquire credentials with AAD with the following exception: '{ex}'");
            }

            if (result == null)
            {
                // Something went wrong during AAD auth, return null
                m_logger($"[VssCredentialsFactory] Unable to acquire AAD token.");
                return new VssAadCredential();
            }

            return new VssAadCredential(new VssAadToken(VsoAadConstants.TokenType, result.AccessToken));
        }

        /// <summary>
        /// Tries integrated windows auth first before trying interactive authentication.
        /// </summary>
        /// <remarks>https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/wam</remarks>
        private async Task<AuthenticationResult> CreateVssCredentialsWithAadForWindowsAsync(IPublicClientApplication app, string[] scopes)
        {
            AuthenticationResult result = null;
            try
            {
                result = await app.AcquireTokenByIntegratedWindowsAuth(scopes)
                                  .ExecuteAsync();
                m_logger("[VssCredentialsFactory] Integrated Windows Authentication was successful.");
            }
            catch (MsalUiRequiredException)
            {
                result = await CreateVssCredentialsWithAadInteractiveAsync(app, scopes);
            }
            catch (MsalServiceException serviceException)
            {
                m_logger($"[VssCredentialsFactory] Unable to acquire credentials with interactive Windows AAD auth with the following exception: '{serviceException}'");
            }
            catch (MsalClientException clientException)
            {
                m_logger($"[VssCredentialsFactory] Unable to acquire credentials with interactive Windows AAD auth with the following exception: '{clientException}'");
            }

            return result;
        }

        /// <summary>
        /// Interactive authentication that will open a browser window for a user to sign in if they are not able to get silent auth or integrated windows auth.
        /// </summary>
        private async Task<AuthenticationResult> CreateVssCredentialsWithAadInteractiveAsync(IPublicClientApplication app, string[] scopes)
        {
            m_logger("[VssCredentialsFactory] Using interactive AAD authentication.");
            var result = await app.AcquireTokenInteractive(scopes)
                .WithPrompt(Prompt.SelectAccount)
                .ExecuteAsync();
            return result;
        }

        /// <summary>
        /// Converts a PAT secure string to a VssCredential.
        /// </summary>
        public VssCredentials GetPatCredentials(SecureString pat)
        {
            return new VssBasicCredential(new NetworkCredential(string.Empty, pat));
        }
    }
}
