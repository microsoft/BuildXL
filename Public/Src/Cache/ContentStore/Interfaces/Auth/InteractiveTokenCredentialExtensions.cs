// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <nodoc/>
    public static class InteractiveTokenCredentialExtensions
    {
        /// <summary>
        /// Interactively authenticates a user with a <see cref="TokenCredential"/> that is either a <see cref="InteractiveBrowserCredential"/> or
        /// a <see cref="DeviceCodeCredential"/>.
        /// </summary>
        /// <remarks>
        /// This method tries to compensate from what looks like a missing common interface between <see cref="InteractiveBrowserCredential"/>
        /// and <see cref="DeviceCodeCredential"/> that represents the ability to interactively authenticate
        /// </remarks>
        /// <param name="tokenCredential">The <see cref="InteractiveBrowserCredential"/> or <see cref="DeviceCodeCredential"/> to perform the authentication request with</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> controlling the request lifetime.</param>
        /// <returns>The <see cref="AuthenticationRecord"/> of the authenticated account.</returns>
        public static Task<AuthenticationRecord> AuthenticateAsync(this TokenCredential tokenCredential, CancellationToken cancellationToken = default)
        {
            if (tokenCredential is InteractiveBrowserCredential interactiveBrowserCredential)
            {
                return interactiveBrowserCredential.AuthenticateAsync(cancellationToken);
            }
            else
            {
                var deviceCodeCredential = tokenCredential as DeviceCodeCredential;
                Contract.Assert(deviceCodeCredential != null, $"Token credential is expected to be either a {nameof(InteractiveBrowserCredential)} or a {nameof(DeviceCodeCredential)}");
                return deviceCodeCredential.AuthenticateAsync(cancellationToken);
            }
        }

        /// <summary>
        /// Sets the authentication record captured from a previous authentication attempt to the credential options, that is expected to be either a
        /// <see cref="InteractiveBrowserCredentialOptions"/> or a <see cref="DeviceCodeCredentialOptions"/>
        /// </summary>
        /// <remarks>
        /// This method tries to compensate from what looks like a missing common interface between <see cref="InteractiveBrowserCredentialOptions"/>
        /// and <see cref="DeviceCodeCredentialOptions"/> that represents the ability capture a record from a previous authentication.  There is an
        /// ISupportsTokenCachePersistenceOptions, but that interface is internal!
        /// </remarks>
        public static void SetAuthenticationRecord(this TokenCredentialOptions tokenCredentialOptions, AuthenticationRecord record)
        {
            if (tokenCredentialOptions is InteractiveBrowserCredentialOptions interactiveBrowserCredentialOptions)
            {
                interactiveBrowserCredentialOptions.AuthenticationRecord = record;
            }
            else
            {
                var deviceCodeOptions = tokenCredentialOptions as DeviceCodeCredentialOptions;
                Contract.Assert(deviceCodeOptions != null, $"Token credential options is expected to be either {nameof(InteractiveBrowserCredentialOptions)} or {nameof(DeviceCodeCredentialOptions)}");
                deviceCodeOptions.AuthenticationRecord = record;
            }
        }

        /// <summary>
        /// Specifies the <see cref="TokenCachePersistenceOptions"/> to be used by the credential. Options must be a
        /// <see cref="InteractiveBrowserCredentialOptions"/> or a <see cref="DeviceCodeCredentialOptions"/>
        /// </summary>
        /// <remarks>
        /// This method tries to compensate from what looks like a missing common interface between <see cref="InteractiveBrowserCredentialOptions"/>
        /// and <see cref="DeviceCodeCredentialOptions"/> that represents the ability capture a record from a previous authentication. There is an
        /// ISupportsTokenCachePersistenceOptions, but that interface is internal!
        /// </remarks>
        public static void SetTokenCachePersistenceOptions(this TokenCredentialOptions tokenCredentialOptions, TokenCachePersistenceOptions tokenCachePersistenceOptions)
        {
            if (tokenCredentialOptions is InteractiveBrowserCredentialOptions interactiveBrowserCredentialOptions)
            {
                interactiveBrowserCredentialOptions.TokenCachePersistenceOptions = tokenCachePersistenceOptions;
            }
            else
            {
                var deviceCodeOptions = tokenCredentialOptions as DeviceCodeCredentialOptions;
                Contract.Assert(deviceCodeOptions != null, $"Token credential options is expected to be either {nameof(InteractiveBrowserCredentialOptions)} or {nameof(DeviceCodeCredentialOptions)}");
                deviceCodeOptions.TokenCachePersistenceOptions = tokenCachePersistenceOptions;
            }
        }
    }
}
