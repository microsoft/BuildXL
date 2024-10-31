// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Identity.Broker;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Core.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Provides interactive credentials for the cache client to authenticate
/// </summary>
/// <remarks>
/// Returned credentials try, in order, <see cref="VisualStudioCodeCredential"/>, <see cref="InteractiveBrowserCredential"/> and <see cref="DeviceCodeCredential"/>.
/// For the interactive browser and device code case, the authentication record that allows a maybe silent authentication is stored in a file to be able to reuse it
/// across build invocations.
/// </remarks>
public class InteractiveClientStorageCredentials : AzureStorageCredentialsBase
{
    private readonly ChainedTokenCredential _credentials;

    /// <inheritdoc/>
    protected override TokenCredential Credentials => _credentials;

    private readonly Tracing.Context _tracingContext;

    /// <nodoc />
    public InteractiveClientStorageCredentials(Tracing.Context tracingContext, string interactiveAuthTokenDirectory, Uri blobUri, IConsole console, CancellationToken cancellationToken) : base(blobUri)
    {
        _tracingContext = tracingContext;
        _credentials = new ChainedTokenCredential(
            new VisualStudioCodeCredential(),
            // On Linux, check whether X server is available by querying DISPLAY. Without an X server, the interactive browser credential provider won't
            // be able to launch a browser. In that case, launch a device code credential provider.
            OperatingSystemHelper.IsLinuxOS && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                ? CreateDeviceCodeWithPersistence(interactiveAuthTokenDirectory, blobUri, console, cancellationToken).GetAwaiter().GetResult()
                : CreateInteractiveBrowserCredentialWithPersistence(interactiveAuthTokenDirectory, blobUri, console, cancellationToken).GetAwaiter().GetResult()
            );
    }

    private Task<TokenCredential> CreateInteractiveBrowserCredentialWithPersistence(
        string interactiveAuthTokenDirectory,
        Uri uri,
        IConsole console,
        CancellationToken token)
    {
        InteractiveBrowserCredentialOptions options;

        _tracingContext.Info($"Creating interactive credential options. Console window handler is '{console?.ConsoleWindowHandle.ToString("X")}'", nameof(InteractiveClientStorageCredentials));

        // On Windows we can use an interactive provider with WAM support.
        if (OperatingSystemHelper.IsWindowsOS && console != null && console.ConsoleWindowHandle != IntPtr.Zero)
        {
            _tracingContext.Info($"Using InteractiveBrowserCredentialBrokerOptions (with WAM support)", nameof(InteractiveClientStorageCredentials));
            options = new InteractiveBrowserCredentialBrokerOptions(console.ConsoleWindowHandle)
            {
                UseDefaultBrokerAccount = true
            };
        }
        else
        {
            _tracingContext.Info($"Using InteractiveBrowserCredentialOptions", nameof(InteractiveClientStorageCredentials));
            options = new InteractiveBrowserCredentialOptions();
        }

        return CreateInteractiveCredentialWithPersistence<InteractiveBrowserCredentialOptions>(
            options,
            interactiveCredentialOptions => new InteractiveBrowserCredential(interactiveCredentialOptions),
            interactiveAuthTokenDirectory,
            uri,
            token
        );
    }

    private Task<TokenCredential> CreateDeviceCodeWithPersistence(
        string interactiveAuthTokenDirectory,
        Uri uri,
        IConsole console,
        CancellationToken token)
    {
        var options =  new DeviceCodeCredentialOptions() 
            {
                // Let's provide an explicit device code callback, so we redirect the message
                // with the device code to the console. 
                DeviceCodeCallback = (deviceCodeInfo, cancellationToken) => 
                    {
                        // There might be other console messages being printed after this one, so
                        // use MessageLevel.Warning so the interactive prompt sticks out
                        console.WriteOutputLine(MessageLevel.Warning, "Accessing the shared cache requires interactive user authentication.");
                        console.WriteOutputLine(MessageLevel.Warning, deviceCodeInfo.Message);
                        
                        return Task.CompletedTask;
                    }
            };

        return CreateInteractiveCredentialWithPersistence<DeviceCodeCredentialOptions>(
            options,
            deviceCodeOptions => new DeviceCodeCredential(deviceCodeOptions),
            interactiveAuthTokenDirectory,
            uri,
            token
        );
    }

    private static async Task<TokenCredential> CreateInteractiveCredentialWithPersistence<TTokenCredentialOptions>(
        TTokenCredentialOptions tokenCredentialOptions,
        Func<TTokenCredentialOptions, TokenCredential> createTokenCredential,
        string interactiveAuthTokenDirectory,
        Uri uri,
        CancellationToken token)
            where TTokenCredentialOptions : TokenCredentialOptions
    {
        // We want a different token per uri to authenticate against. So let's just use a hash of the URI, since we want to avoid the final path name
        // to contain disallowed characters.
        var uriAsHash = HashInfoLookup.GetContentHasher(HashType.SHA256).GetContentHash(Encoding.UTF8.GetBytes(uri.ToString())).ToHex();

        var tokenName = $"BxlBlobCacheAuthToken{uriAsHash}";
        // The auth record will be serialized in the designated directory
        var file = Path.Combine(interactiveAuthTokenDirectory, tokenName);

        var tokenOptions = new TokenCachePersistenceOptions { Name = tokenName };
        tokenCredentialOptions.SetTokenCachePersistenceOptions(tokenOptions);

        bool authRecordExists;
        try
        {
            authRecordExists = File.Exists(file);

            if (authRecordExists)
            {
                // Load the previously serialized AuthenticationRecord from disk and deserialize it.
                using var authRecordStream = new FileStream(file, FileMode.Open, FileAccess.Read);
                var serializedAuthRecord = await AuthenticationRecord.DeserializeAsync(authRecordStream, token);
                tokenCredentialOptions.SetAuthenticationRecord(serializedAuthRecord);
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


        var credential = createTokenCredential(tokenCredentialOptions);

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
                    Directory.CreateDirectory(interactiveAuthTokenDirectory);
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
