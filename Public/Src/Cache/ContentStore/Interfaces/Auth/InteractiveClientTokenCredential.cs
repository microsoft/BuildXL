// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
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
public class InteractiveClientTokenCredential : ChainedTokenCredential
{
    /// <summary>
    /// Returned credentials try, in order, <see cref="VisualStudioCodeCredential"/> and <see cref="InteractiveBrowserCredentialOptions"/> (for the latter, <see cref="DeviceCodeCredential"/>
    /// is used instead when no browser is available).
    /// </summary>
    /// <remarks>
    /// For the interactive browser and device code case, the authentication record that allows a maybe silent authentication is stored in a file under the provided directory, to be able to reuse it
    /// across build invocations. The given <paramref name="persistentTokenIdentifier"/> is used as the identifier for the token.
    /// </remarks>
    public InteractiveClientTokenCredential(Tracing.Context tracingContext, string interactiveAuthTokenDirectory, ContentHash persistentTokenIdentifier, IConsole console, CancellationToken cancellationToken)
        : base(new VisualStudioCodeCredential(),
            // On Linux, check whether X server is available by querying DISPLAY. Without an X server, the interactive browser credential provider won't
            // be able to launch a browser. In that case, launch a device code credential provider.
            OperatingSystemHelper.IsLinuxOS && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                ? CreateDeviceCodeWithPersistence(interactiveAuthTokenDirectory, persistentTokenIdentifier, console, cancellationToken).GetAwaiter().GetResult()
                : CreateInteractiveBrowserCredentialWithPersistence(tracingContext, interactiveAuthTokenDirectory, persistentTokenIdentifier, console, cancellationToken).GetAwaiter().GetResult()
            )
    {
    }

    private static Task<TokenCredential> CreateInteractiveBrowserCredentialWithPersistence(
        Tracing.Context tracingContext,
        string interactiveAuthTokenDirectory,
        ContentHash persistentTokenIdentifier,
        IConsole console,
        CancellationToken token)
    {
        InteractiveBrowserCredentialOptions options;

        tracingContext.Info($"Creating interactive credential options. Console window handler is '{console?.ConsoleWindowHandle.ToString("X")}'", nameof(InteractiveClientTokenCredential));

        // On Windows we can use an interactive provider with WAM support.
        if (OperatingSystemHelper.IsWindowsOS && console != null && console.ConsoleWindowHandle != IntPtr.Zero)
        {
            tracingContext.Info($"Using InteractiveBrowserCredentialBrokerOptions (with WAM support)", nameof(InteractiveClientTokenCredential));
            options = new InteractiveBrowserCredentialBrokerOptions(console.ConsoleWindowHandle)
            {
                UseDefaultBrokerAccount = true
            };
        }
        else
        {
            tracingContext.Info($"Using InteractiveBrowserCredentialOptions", nameof(InteractiveClientTokenCredential));
            options = new InteractiveBrowserCredentialOptions();
        }

        return CreateInteractiveCredentialWithPersistence<InteractiveBrowserCredentialOptions>(
            options,
            interactiveCredentialOptions => new InteractiveBrowserCredential(interactiveCredentialOptions),
            interactiveAuthTokenDirectory,
            persistentTokenIdentifier,
            token
        );
    }

    private static Task<TokenCredential> CreateDeviceCodeWithPersistence(
        string interactiveAuthTokenDirectory,
        ContentHash persistentTokenIdentifier,
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
            persistentTokenIdentifier,
            token
        );
    }

    private static async Task<TokenCredential> CreateInteractiveCredentialWithPersistence<TTokenCredentialOptions>(
        TTokenCredentialOptions tokenCredentialOptions,
        Func<TTokenCredentialOptions, TokenCredential> createTokenCredential,
        string interactiveAuthTokenDirectory,
        ContentHash persistentTokenIdentifier,
        CancellationToken token)
            where TTokenCredentialOptions : TokenCredentialOptions
    {
        var uriAsHash = persistentTokenIdentifier.ToHex();

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
        // If the user doesn't respond in time, we cancel the operation. A TokenCredential will throw an AuthenticationFailedException
        // if the cancellation happens while the interactive prompt is ongoing, so we account for that case as well.
        catch (Exception ex) when (ex is OperationCanceledException || (ex is AuthenticationFailedException && tokenSource.Token.IsCancellationRequested))
        {
            // Let's provide a more informative message. The cache factory will catch any exception that happens during creation time
            // and will display the error to the user 
            throw new Exception($"Interactive authentication timed out after {userTimeout.TotalSeconds} seconds.");
        }

        return credential;
    }
}
