// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Security.Cryptography;
using BuildXL.Utilities;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.Storage.Fingerprints
{
    internal static class HashAlgorithmExtensions
    {
        public static HashAlgorithm Create(this HashAlgorithmType type)
        {
            switch (type)
            {
                case HashAlgorithmType.SHA1Managed:
                    return new SHA1Managed();
                case HashAlgorithmType.MurmurHash3:
                    return new MurmurHashEngine();
                default:
                    Contract.Check(false)?.Assert($"Unknown hash algorithm type: {type}");
                    return null;
            }
        }
    }
}