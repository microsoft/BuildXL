// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.Identity.Broker;
using BuildXL.Utilities.Core.Tracing;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Provides interactive credentials for a client to authenticate
/// </summary>
public class InteractiveClientTokenCredential : ChainedTokenCredential
{
    /// <summary>
    /// Returned credentials try, in order, <see cref="VisualStudioCodeCredential"/> and <see cref="InteractiveBrowserCredentialOptions"/> (for the latter, <see cref="DeviceCodeCredential"/>
    /// is used instead when no browser is available).
    /// </summary>
    /// <remarks>
    /// For the interactive browser and device code case, the authentication record that allows a maybe silent authentication is stored in a file under the provided directory, to be able to reuse it
    /// across build invocations. The given <paramref name="tenantId"/> is used as the identifier for the token.
    /// </remarks>
    public InteractiveClientTokenCredential(
        Action<string> debugLogger,
        string deviceCodeUserFacingMessage,
        string tenantId, 
        IConsole console, 
        CancellationToken cancellationToken)
        : base(new VisualStudioCodeCredential(),
            // On Linux, check whether X server is available by querying DISPLAY. Without an X server, the interactive browser credential provider won't
            // be able to launch a browser. In that case, launch a device code credential provider.
            OperatingSystemHelper.IsLinuxOS && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                ? CreateDeviceCodeWithPersistence(deviceCodeUserFacingMessage, tenantId, console, cancellationToken).GetAwaiter().GetResult()
                : CreateInteractiveBrowserCredentialWithPersistence(debugLogger, tenantId, console, cancellationToken).GetAwaiter().GetResult()
            )
    {
    }

    private static Task<TokenCredential> CreateInteractiveBrowserCredentialWithPersistence(
        Action<string> debugLogger,
        string tenantId,
        IConsole console,
        CancellationToken token)
    {
        InteractiveBrowserCredentialOptions options;

        debugLogger.Invoke($"Creating interactive credential options. Console window handler is '{console?.ConsoleWindowHandle.ToString("X")}'");

        // On Windows we can use an interactive provider with WAM support.
        if (OperatingSystemHelper.IsWindowsOS && console != null && console.ConsoleWindowHandle != IntPtr.Zero)
        {
            debugLogger.Invoke($"Using InteractiveBrowserCredentialBrokerOptions (with WAM support)");
            options = new InteractiveBrowserCredentialBrokerOptions(console.ConsoleWindowHandle)
            {
                UseDefaultBrokerAccount = true
            };
        }
        else
        {
            debugLogger.Invoke($"Using InteractiveBrowserCredentialOptions");
            options = new InteractiveBrowserCredentialOptions();
        }

        return CreateInteractiveCredentialWithPersistence<InteractiveBrowserCredentialOptions>(
            options,
            (interactiveCredentialOptions, tokenCredentialOptions) => 
            { 
                interactiveCredentialOptions.TokenCachePersistenceOptions = tokenCredentialOptions; 
                return new InteractiveBrowserCredential(interactiveCredentialOptions); 
            },
            tenantId,
            token
        );
    }

    private static Task<TokenCredential> CreateDeviceCodeWithPersistence(
        string deviceCodeUserFacingMessage,
        string tenantId,
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
                        console.WriteOutputLine(MessageLevel.Warning, deviceCodeUserFacingMessage);
                        console.WriteOutputLine(MessageLevel.Warning, deviceCodeInfo.Message);
                        
                        return Task.CompletedTask;
                    }
            };

        return CreateInteractiveCredentialWithPersistence<DeviceCodeCredentialOptions>(
            options,
            (deviceCodeOptions, tokenCredentialOptions) => {
                deviceCodeOptions.TokenCachePersistenceOptions = tokenCredentialOptions;
                return new DeviceCodeCredential(deviceCodeOptions);
            },
            tenantId,
            token
        );
    }

    private static async Task<TokenCredential> CreateInteractiveCredentialWithPersistence<TTokenCredentialOptions>(
        TTokenCredentialOptions tokenCredentialOptions,
        Func<TTokenCredentialOptions, TokenCachePersistenceOptions, TokenCredential> createTokenCredential,
        string tenantId,
        CancellationToken token)
            where TTokenCredentialOptions : TokenCredentialOptions
    {
        // This name makes it able to share a token cache with the Microsoft authored, open source, shared azureauth.exe library here: https://github.com/AzureAD/microsoft-authentication-cli/tree/8de5747255e4543dca0cbf77f1f0ee6dc0c83d7e
        var tokenName = $"msal_{tenantId}.cache";

        var tokenOptions = new TokenCachePersistenceOptions { Name = tokenName };
        var credential = createTokenCredential(tokenCredentialOptions, tokenOptions);

        // The interactive browser credential unfortunately doesn't offer a timeout configuration. Let's
        // externally set a 90s timeout for the user to respond 
        var userTimeout = TimeSpan.FromSeconds(90);
        var internalTokenSource = new CancellationTokenSource();
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(internalTokenSource.Token, token);

        try
        {
            internalTokenSource.CancelAfter(userTimeout);
            await credential.GetTokenAsync(new TokenRequestContext(new string[] { "https://management.azure.com//.default" }), tokenSource.Token);
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
