// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Security;
using System.Threading.Tasks;
using JetBrains.Annotations;
#if FEATURE_CORECLR
using Microsoft.IdentityModel.Clients.ActiveDirectory;
#endif
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;
#if !PLATFORM_OSX
using Microsoft.VisualStudio.Services.Content.Common.Authentication;
#else
using BuildXL.Cache.ContentStore.Exceptions;
#endif

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     Factory for asynchronously creating VssCredentials
    /// </summary>
    public class VssCredentialsFactory
    {
        private readonly VssCredentials _credentials;

#if !PLATFORM_OSX
        private readonly string _userName;
        private readonly SecureString _pat;
        private readonly byte[] _credentialBytes;

        private readonly VsoCredentialHelper _helper;

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        public VssCredentialsFactory(VsoCredentialHelper helper)
        {
            _helper = helper;
        }

        /// <summary>
        /// Initializes a new instance with a helper and a user name explicitly provided.
        /// </summary>
        /// <remarks>
        /// CoreCLR is not allowed to query the OS for the AAD user name of the current user,
        /// which is why this constructor allows for that user name to be explicitly provided.
        /// </remarks>
        public VssCredentialsFactory(VsoCredentialHelper helper, [CanBeNull] string userName)
            : this(helper)
        {
            _userName = userName;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, VssCredentials credentials)
            : this(helper)
        {
            _credentials = credentials;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, SecureString pat)
            : this(helper)
        {
            _pat = pat;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        public VssCredentialsFactory(VsoCredentialHelper helper, byte[] value)
            : this(helper)
        {
            _credentialBytes = value;
        }
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="VssCredentialsFactory"/> class.
        /// </summary>
        public VssCredentialsFactory(VssCredentials creds)
        {
            _credentials = creds;
        }

#if !PLATFORM_OSX
#if FEATURE_CORECLR
        private const string VsoAadSettings_ProdAadAddress = "https://login.windows.net/";
        private const string VsoAadSettings_TestAadAddress = "https://login.windows-ppe.net/";
        private const string VsoAadSettings_DefaultTenant = "microsoft.com";

        private VssCredentials CreateVssCredentialsForUserName(Uri baseUri)
        {
            var authorityAadAddres = baseUri.Host.ToLowerInvariant().Contains("visualstudio.com")
                ? VsoAadSettings_ProdAadAddress
                : VsoAadSettings_TestAadAddress;
            var authCtx = new AuthenticationContext(authorityAadAddres + VsoAadSettings_DefaultTenant);

            var userCred = string.IsNullOrEmpty(_userName)
                ? new UserCredential() 
                : new UserCredential(_userName);

            var token = new VssAadToken(authCtx, userCred, VssAadTokenOptions.None);
            token.AcquireToken(); 
            return new VssAadCredential(token);
        }
#endif //FEATURE_CORECLR

        /// <summary>
        /// Creates a VssCredentials object and returns it.
        /// </summary>
        public async Task<VssCredentials> CreateVssCredentialsAsync(Uri baseUri, bool useAad)
        {
            if (_credentials != null)
            {
                return _credentials;
            }

            if (_pat != null)
            {
                return _helper.GetPATCredentials(_pat);
            }

#if FEATURE_CORECLR
            // If the user name is explicitly provided call a different auth method that's
            // not going to query the OS for the AAD user name (which is, btw, disallowed on CoreCLR).
            if (_userName != null)
            {
                return CreateVssCredentialsForUserName(baseUri);
            }
#endif // FEATURE_CORECLR
            return await _helper.GetCredentialsAsync(baseUri, useAad, _credentialBytes, null)
                .ConfigureAwait(false);
        }
#else
        /// <summary>
        /// Creates a VssCredentials object and returns it.
        /// </summary>
        public Task<VssCredentials> CreateVssCredentialsAsync(Uri baseUri, bool useAad)
        {
            if (_credentials != null)
            {
                return Task.FromResult(_credentials);
            }

            throw new CacheException("CoreCLR on non-windows platforms only allows PAT based VSTS authentication!");
        }
#endif
    }
}
