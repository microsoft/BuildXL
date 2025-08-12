// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.Core;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Tool.BlobDaemon
{
    /// <summary>
    /// Simple credentials based on a static bearer token.
    /// </summary>
    internal sealed class StaticTokenCredential : TokenCredential
    {
        private readonly string m_accessToken;

        public StaticTokenCredential(string accessToken)
        {
            m_accessToken = accessToken;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            // Expiration value is a dummy value. It's used by Azure SDK to decide when to refresh the token.
            // Our token is static (i.e., there is nothing to refresh), and users are in charge of ensuring
            // that it is valid for the duration of the build.
            return new AccessToken(m_accessToken, DateTimeOffset.UtcNow.AddDays(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            // Expiration value is a dummy value. It's used by Azure SDK to decide when to refresh the token.
            // Our token is static (i.e., there is nothing to refresh), and users are in charge of ensuring
            // that it is valid for the duration of the build.
            return new ValueTask<AccessToken>(new AccessToken(m_accessToken, DateTimeOffset.UtcNow.AddDays(1)));
        }
    }
}
