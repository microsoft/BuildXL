// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using Azure.Core;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Provides credentials by interacting with the codespaces extension azure-auth-helper tool (see https://github.com/microsoft/ado-codespaces-auth)
/// </summary>
/// <remarks>
/// The extension tool is typically made available via PATH in a codespaces installation
/// </remarks>
public class CodespacesCredentials : AzureStorageCredentialsBase
{
    private readonly AzureAuthTokenCredential _credentials;

    /// <summary>
    /// The name of the auth helper tool
    /// </summary>
    public const string AuthHelperToolName = "azure-auth-helper";

    /// <nodoc />
    public CodespacesCredentials(string authHelperToolPath, Uri blobUri) : base(blobUri)
    {
        Contract.Requires(authHelperToolPath != null);
        _credentials = new AzureAuthTokenCredential(authHelperToolPath);
    }

    /// <inheritdoc/>
    protected override TokenCredential Credentials => _credentials;

    /// <summary>
    /// Tries to find the azure-auth-helper tool in PATH.
    /// </summary>
    /// <returns>The full path to the tool or null if the tool is not found</returns>
    /// <param name="failure">The exception message, if any exceptions occurred during the lookup</param>
    /// <remarks>The presence of the tool can be used as an indication of whether this authentication method is available</remarks>
    public static string? FindAuthHelperTool(out string? failure)
        => FindAuthHelperToolInternal("PATH", out failure);

    /// <summary>
    /// For testing purposes, allows to specify the environment variable to use as PATH
    /// </summary>
    internal static string? FindAuthHelperToolForTesting(string environmentVariableName, out string? failure)
        => FindAuthHelperToolInternal(environmentVariableName, out failure);

    private static string? FindAuthHelperToolInternal(string environmentVariableName, out string? failure)
    {
        failure = null;
        try
        {
            var directory = Environment
                .GetEnvironmentVariable(environmentVariableName)
                ?.Split(Path.PathSeparator)
                .FirstOrDefault(path => File.Exists(Path.Combine(path, AuthHelperToolName)));

            if (directory == null)
            {
                return null;
            }

            return Path.Combine(directory, AuthHelperToolName);
        }
        catch (Exception ex) 
        {
            // If there is any problem, just record the failure for logging purposes and claim that it was not found
#pragma warning disable EPC12 // Suspicious exception handling: only the 'Message' property is observed in the catch block
            failure = ex.Message;
#pragma warning restore EPC12 // Suspicious exception handling: only the 'Message' property is observed in the catch block
            return null;
        }
    }
}
