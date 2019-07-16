// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Type to signal the host which kind of secret is expected to be returned
    /// </summary>
    public struct RetrieveSecretsRequest
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
