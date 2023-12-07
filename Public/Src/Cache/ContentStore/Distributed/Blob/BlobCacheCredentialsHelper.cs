// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Utilities.Collections;

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    public static class BlobCacheCredentialsHelper
    {
        public enum FileEncryption
        {
            None,
            Dpapi,
        }

        /// <summary>
        /// Load credentials from a file.
        /// </summary>
        public static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> Load(AbsolutePath path, FileEncryption encryption)
        {
            string credentials;
            switch (encryption)
            {
                case FileEncryption.None:
                    credentials = File.ReadAllText(path.Path);
                    break;
                case FileEncryption.Dpapi:
#if NET5_0_OR_GREATER
                    if (!OperatingSystem.IsWindows())
                    {
                        throw new NotSupportedException("Encrypted credentials are only supported on Windows");
                    }

                    var bytes = File.ReadAllBytes(path.Path);
#pragma warning disable CA1416 // Platform compatibility is checked above
                    var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.LocalMachine);
#pragma warning restore CA1416
                    credentials = Encoding.UTF8.GetString(decrypted);
                    break;
#else
                    throw new NotSupportedException("Encrypted credentials are only supported on .NET 5.0 or greater");
#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(encryption), encryption, null);
            }
            Contract.Assert(!string.IsNullOrEmpty(credentials));

            return ParseFromFileFormat(credentials);
        }

        public static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> ParseFromEnvironmentFormat(string environmentVariableContents)
        {
            var credentials = new Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials>();
            credentials.AddRange(
                environmentVariableContents.Split(' ')
                    .Select(
                        secret =>
                        {
                            var credential = new SecretBasedAzureStorageCredentials(secret.Trim());
                            var accountName = BlobCacheStorageAccountName.Parse(credential.GetAccountName());
                            return new KeyValuePair<BlobCacheStorageAccountName, IAzureStorageCredentials>(accountName, credential);
                        }));

            return credentials;
        }


        /// <summary>
        /// This method supports two formats:
        /// 1: A dictionary of name to secret
        /// 2: A list of strings with the format: "[AccountKey];[AccountUrl]"
        /// </summary>
        /// <remarks>
        /// This code is also used in CloudBuild to support this same exact format.
        /// </remarks>
        public static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> ParseFromFileFormat(string credentials)
        {
            try
            {
                return JsonUtilities.JsonDeserialize<Dictionary<string, string>>(credentials).ToDictionary(
                    kv => BlobCacheStorageAccountName.Parse(kv.Key),
                    kv => (IAzureStorageCredentials)new SecretBasedAzureStorageCredentials(
                        new PlainTextSecret($"DefaultEndpointsProtocol=https;AccountName={kv.Key};AccountKey={kv.Value}")));
            }
            catch (Exception ex1)
            {
                try
                {
                    List<string> keyUrlPairs = JsonUtilities.JsonDeserialize<List<string>>(credentials);
                    IEnumerable<IAzureStorageCredentials> creds = keyUrlPairs
                        .Select(
                            keyUrlPair =>
                            {
                                string[] segments = keyUrlPair.Split(';');
                                string key = segments[0];
                                string urlString = segments[1];

                                var url = new Uri(urlString);
                                string accountName = url.Host.Split('.')[0];

                                string connectionString = $"DefaultEndpointsProtocol=https;EndpointSuffix=core.windows.net;" +
                                                          $"AccountName={accountName};AccountKey={key};BlobEndpoint={urlString}";

                                return new SecretBasedAzureStorageCredentials(new PlainTextSecret(connectionString));
                            });

                    return creds.ToDictionary(
                        cred => BlobCacheStorageAccountName.Parse(cred.GetAccountName()),
                        cred => cred);
                }
                catch (Exception ex2)
                {
                    throw new AggregateException(message: "Failed to parse Blob Cache credentials", ex1, ex2);
                }
            }
        }

    }
}
