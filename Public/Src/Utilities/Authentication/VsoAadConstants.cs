// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Authentication
{
    /// <summary>
    /// Set of constants to be used for Vso authentication
    /// CODESYNC: (Cloudbuild) private/Cache/Client/MemoizationClient/VssCredentialsUtil.cs
    /// </summary>
    public class VsoAadConstants
    {
        /// <summary>
        /// Constant value to target Azure DevOps
        /// MSAL scope representation of the VSO service principal
        /// </summary>
        public const string Scope = "499b84ac-1321-427f-aa17-267ca6975798/.default";
        /// <summary>
        /// Visual Studio IDE client ID originally provisioned by Azure Tools.
        /// </summary>
        public const string Client = "872cd9fa-d31f-45e0-9eab-6e460a02d1f1";
        /// <summary>
        /// TenantId for Microsoft tenant
        /// </summary>
        public const string MicrosoftTenantId = "72f988bf-86f1-41af-91ab-2d7cd011db47";
        /// <summary>
        /// The type of Token returned
        /// </summary>
        public const string TokenType = "Bearer";
        /// <summary>
        /// NET Core apps will need a redirect uri to retrieve the token, localhost picks an open port by default
        /// https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/wiki/System-Browser-on-.Net-Core
        /// </summary>
        public const string RedirectUri = "http://localhost";
    }
}
