// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// A <see cref="TokenCredential"/> that returns a pre-obtained access token.
    /// </summary>
    /// <remarks>
    /// This is useful when a token is obtained externally (e.g., via a CI pipeline or a separate authentication tool)
    /// and passed to the cache, avoiding the need for interactive browser-based authentication.
    /// The token is returned as-is with a provided expiration time.
    /// </remarks>
    public class StaticAccessTokenCredential : TokenCredential
    {
        private readonly AccessToken _accessToken;

        /// <nodoc />
        public StaticAccessTokenCredential(string accessToken, TimeSpan expiration)
        {
            _accessToken = new AccessToken(accessToken, DateTimeOffset.UtcNow + expiration);
        }

        /// <inheritdoc />
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return _accessToken;
        }

        /// <inheritdoc />
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return new ValueTask<AccessToken>(_accessToken);
        }
    }
}
