// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using BuildXL.Cache.ContentStore.Hashing;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Provides interactive credentials for the cache client to authenticate
/// </summary>
/// <remarks>
/// Returned credentials try, in order, <see cref="VisualStudioCodeCredential"/> and <see cref="InteractiveBrowserCredential"/>.
/// For the interactive browser case, the authentication record that allows a maybe silent authentication is stored in a file to be able to reuse it
/// across build invocations.
/// </remarks>
public class InteractiveClientStorageCredentials : AzureStorageCredentialsBase
{
    private readonly ChainedTokenCredential _credentials;

    /// <inheritdoc/>
    protected override TokenCredential Credentials => _credentials;

    /// <nodoc />
    public InteractiveClientStorageCredentials(string interactiveAuthTokenDirectory, Uri blobUri, CancellationToken cancellationToken) : base(blobUri)
    {
        _credentials = new ChainedTokenCredential(
            new VisualStudioCodeCredential(),
            CreateInteractiveBrowserCredentialWithPersistence(interactiveAuthTokenDirectory, blobUri, cancellationToken).GetAwaiter().GetResult());
    }

    private static async Task<InteractiveBrowserCredential> CreateInteractiveBrowserCredentialWithPersistence(string interactiveAuthTokenDirectory, Uri uri, CancellationToken token)
    {
        // We want a different token per uri to authenticate against. So let's just use a VSO hash of the URI, since we want to avoid the final path name
        // to contain disallowed characters.
        var uriAsHash = HashInfoLookup.GetContentHasher(HashType.Vso0).GetContentHash(Encoding.UTF8.GetBytes(uri.ToString())).ToHex();

        var tokenName = $"BxlBlobCacheAuthToken{uriAsHash}";
        // The auth record will be serialized in the designated directory
        var file = Path.Combine(interactiveAuthTokenDirectory, tokenName);

        var options = new InteractiveBrowserCredentialOptions
        {
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = tokenName },
        };

        bool authRecordExists;
        try
        {
            authRecordExists = File.Exists(file);

            if (authRecordExists)
            {
                // Load the previously serialized AuthenticationRecord from disk and deserialize it.
                using var authRecordStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var serializedAuthRecord = await AuthenticationRecord.DeserializeAsync(authRecordStream, token);
                options.AuthenticationRecord = serializedAuthRecord;
            }
        }
#pragma warning disable ERP022
        catch
        {
            // Retrieving the authentication record is best effort basis. If any problem occurs, we just
            // don't set it
            // Let's catch the case of the file being there but having any deserialization issue. In that case
            // we just pretend the file is not there.
            authRecordExists = false;
        }
#pragma warning restore ERP022


        var credential = new InteractiveBrowserCredential(options);

        // The interactive browser credential unfortunately doesn't offer a timeout configuration. Let's
        // externally set a 90s timeout for the user to respond 
        var userTimeout = TimeSpan.FromSeconds(90);
        var internalTokenSource = new CancellationTokenSource();
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(internalTokenSource.Token, token);

        try
        {
            internalTokenSource.CancelAfter(userTimeout);

            // If the record is there, attempt silent authentication via GetTokenAsync. This may prompt the user if silent authentication is not available.
            // Otherwise, call AuthenticateAsync, which always prompts the user, and try to serialize the auth token for later reuse.
            if (authRecordExists)
            {
                await credential.GetTokenAsync(new TokenRequestContext(new string[] { "https://management.azure.com//.default" }), tokenSource.Token);
            }
            else
            { 
                var authRecord = await credential.AuthenticateAsync(tokenSource.Token);
            
                try
                {
                    using var authRecordStream = new FileStream(file, FileMode.Create, FileAccess.Write);
                    await authRecord.SerializeAsync(authRecordStream, token);
                }
#pragma warning disable ERP022
                catch
                {
                    // Serializing the authentication record is best effort basis. If any problem occurs, we just
                    // don't store it
                }
#pragma warning restore ERP022
            }
        }
        catch (OperationCanceledException)
        {
            // Let's provide a more informative message. The cache factory will catch any exception that happens during creation time
            // and will display the error to the user 
            throw new Exception($"Browser interactive authentication timed out after {userTimeout.TotalSeconds} seconds.");
        }

        return credential;
    }
}
