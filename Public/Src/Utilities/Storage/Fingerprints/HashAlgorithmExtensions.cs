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
#pragma warning disable SYSLIB0021 // Type or member is obsolete. Temporarily suppressing the warning for .net 6. Work item: 1885580
                    return new SHA1Managed();
#pragma warning restore SYSLIB0021 // Type or member is obsolete
                case HashAlgorithmType.MurmurHash3:
                    return new MurmurHashEngine();
                default:
                    Contract.Check(false)?.Assert($"Unknown hash algorithm type: {type}");
                    return null;
            }
        }
    }
}