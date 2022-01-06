// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.Json;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// A helper type used for serializing <see cref="RetrievedSecrets"/> to and from a string.
    /// </summary>
    internal static class RetrievedSecretsSerializer
    {
        public const string SerializedSecretsKeyName = "SerializedSecretsKey";

        public static string Serialize(RetrievedSecrets secrets)
        {
            var secretsList = secrets.Secrets
                .Select(kvp => SecretData.FromSecret(kvp.Key, kvp.Value))
                .OrderBy(s => s.Name)
                .ToList();

            return JsonSerializer.Serialize(secretsList);
        }

        public static Result<RetrievedSecrets> Deserialize(string content)
        {
            Contract.Requires(!string.IsNullOrEmpty(content));

            List<SecretData> secrets = JsonSerializer.Deserialize<List<SecretData>>(content);
            return Result.Success(
                new RetrievedSecrets(
                    secrets.ToDictionary(s => s.Name, s => s.ToSecret())));

        }

        internal class SecretData
        {
            public string Name { get; set; }
            public SecretKind Kind { get; set; }

            public string SecretOrToken { get; set; }

            public string StorageAccount { get; set; }

            public string ResourcePath { get; set; }

            public Secret ToSecret()
                => Kind switch
                {
                    SecretKind.PlainText => new PlainTextSecret(SecretOrToken),
                    SecretKind.SasToken => new UpdatingSasToken(new SasToken(SecretOrToken, StorageAccount, ResourcePath)),
                    _ => throw new ArgumentOutOfRangeException(nameof(Kind))
                };

            public static SecretData FromSecret(string name, Secret secret)
                => secret switch
                {
                    PlainTextSecret plainTextSecret
                        => new SecretData { Name = name, Kind = SecretKind.PlainText, SecretOrToken = plainTextSecret.Secret },

                    UpdatingSasToken updatingSasToken
                        => new SecretData
                           {
                               Name = name,
                               Kind = SecretKind.SasToken,
                               SecretOrToken = updatingSasToken.Token.Token,
                               ResourcePath = updatingSasToken.Token.ResourcePath,
                               StorageAccount = updatingSasToken.Token.StorageAccount
                           },

                    _ => throw new ArgumentOutOfRangeException(nameof(secret))
                };
        }
    }
}
