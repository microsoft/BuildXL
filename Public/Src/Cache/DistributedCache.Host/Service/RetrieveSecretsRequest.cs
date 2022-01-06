// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Type to signal the host which kind of secret is expected to be returned
    /// </summary>
    public readonly struct RetrieveSecretsRequest
    {
        public string Name { get; }

        public SecretKind Kind { get; }

        public RetrieveSecretsRequest(string name, SecretKind kind)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Name = name;
            Kind = kind;
        }
    }
}
