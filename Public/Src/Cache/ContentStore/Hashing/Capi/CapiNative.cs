// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;

#if FEATURE_CORESYSTEM
using System.Core;
#endif

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Native interop with CAPI. Native definitions can be found in wincrypt.h or msaxlapi.h
    /// </summary>
    internal static class CapiNative
    {
        internal enum AlgorithmClass
        {
            DataEncryption = (3 << 13),         // ALG_CLASS_DATA_ENCRYPT
            Hash = (4 << 13)                    // ALG_CLASS_HASH
        }

        internal enum AlgorithmType
        {
            Any = (0 << 9),                     // ALG_TYPE_ANY
            Block = (3 << 9)                    // ALG_TYPE_BLOCK
        }

        internal enum AlgorithmSubId
        {
            MD5 = 3,                            // ALG_SID_MD5
            Sha1 = 4,                           // ALG_SID_SHA1
            Sha256 = 12,                        // ALG_SID_SHA_256
            Sha384 = 13,                        // ALG_SID_SHA_384
            Sha512 = 14,                        // ALG_SID_SHA_512

            Aes128 = 14,                        // ALG_SID_AES_128
            Aes192 = 15,                        // ALG_SID_AES_192
            Aes256 = 16                         // ALG_SID_AES_256
        }

        internal enum AlgorithmId
        {
            None = 0,

            Aes128 = (AlgorithmClass.DataEncryption | AlgorithmType.Block | AlgorithmSubId.Aes128),     // CALG_AES_128
            Aes192 = (AlgorithmClass.DataEncryption | AlgorithmType.Block | AlgorithmSubId.Aes192),     // CALG_AES_192
            Aes256 = (AlgorithmClass.DataEncryption | AlgorithmType.Block | AlgorithmSubId.Aes256),     // CALG_AES_256

            MD5 = (AlgorithmClass.Hash | AlgorithmType.Any | AlgorithmSubId.MD5),                       // CALG_MD5
            Sha1 = (AlgorithmClass.Hash | AlgorithmType.Any | AlgorithmSubId.Sha1),                     // CALG_SHA1
            Sha256 = (AlgorithmClass.Hash | AlgorithmType.Any | AlgorithmSubId.Sha256),                 // CALG_SHA_256
            Sha384 = (AlgorithmClass.Hash | AlgorithmType.Any | AlgorithmSubId.Sha384),                 // CALG_SHA_384
            Sha512 = (AlgorithmClass.Hash | AlgorithmType.Any | AlgorithmSubId.Sha512)                  // CALG_SHA_512
        }

        /// <summary>
        ///     Flags for the CryptAcquireContext API
        /// </summary>
        [Flags]
        internal enum CryptAcquireContextFlags
        {
            None = 0x00000000,
            VerifyContext = unchecked((int)0xF0000000)      // CRYPT_VERIFYCONTEXT
        }

        /// <summary>
        ///     Error codes returned from CAPI
        /// </summary>
        internal enum ErrorCode
        {
            Success = 0x00000000,                                       // ERROR_SUCCESS
            MoreData = 0x00000ea,                                       // ERROR_MORE_DATA
            NoMoreItems = 0x00000103,                                   // ERROR_NO_MORE_ITEMS
            BadData = unchecked((int)0x80090005),                       // NTE_BAD_DATA
            BadAlgorithmId = unchecked((int)0x80090008),                // NTE_BAD_ALGID
            ProviderTypeNotDefined = unchecked((int)0x80090017),        // NTE_PROV_TYPE_NOT_DEF
            KeysetNotDefined = unchecked((int)0x80090019)               // NTE_KEYSET_NOT_DEF
        }

        /// <summary>
        ///     Parameters that GetHashParam can query
        /// </summary>
        internal enum HashParameter
        {
            None = 0x0000,
            AlgorithmId = 0x0001,           // HP_ALGID
            HashValue = 0x0002,             // HP_HASHVAL
            HashSize = 0x0004               // HP_HASHSIZE
        }

        /// <summary>
        ///     Well-known names of crypto service providers
        /// </summary>
        internal static class ProviderNames
        {
            // MS_ENH_RSA_AES_PROV
            public const string MicrosoftEnhancedRsaAes = "Microsoft Enhanced RSA and AES Cryptographic Provider";
            public const string MicrosoftEnhancedRsaAesPrototype = "Microsoft Enhanced RSA and AES Cryptographic Provider (Prototype)";
        }

        /// <summary>
        ///     Provider type accessed in a crypto service provider. These provide the set of algorithms
        ///     available to use for an application.
        /// </summary>
        internal enum ProviderType
        {
            None = 0,
            RsaAes = 24         // PROV_RSA_AES
        }

        internal const uint ALG_CLASS_SIGNATURE = (1 << 13);
        internal const uint ALG_TYPE_RSA = (2 << 9);
        internal const uint ALG_SID_RSA_ANY = 0;
        internal const uint ALG_SID_DSS_ANY = 0;
        internal const uint ALG_TYPE_DSS = (1 << 9);
        internal const uint ALG_CLASS_KEY_EXCHANGE = (5 << 13);

        internal const uint CALG_RSA_SIGN = (ALG_CLASS_SIGNATURE | ALG_TYPE_RSA | ALG_SID_RSA_ANY);
        internal const uint CALG_DSS_SIGN = (ALG_CLASS_SIGNATURE | ALG_TYPE_DSS | ALG_SID_DSS_ANY);
        internal const uint CALG_RSA_KEYX = (ALG_CLASS_KEY_EXCHANGE | ALG_TYPE_RSA | ALG_SID_RSA_ANY);
        internal const uint CNG_RSA_PUBLIC_KEY_BLOB = 72;
        internal const uint X509_DSS_PUBLICKEY = 38;
        internal const uint X509_DSS_PARAMETERS = 39;

        internal const uint X509_ASN_ENCODING = 0x00000001;
        internal const uint PKCS_7_ASN_ENCODING = 0x00010000;

        internal const uint CRYPT_OID_INFO_OID_KEY = 1;

        internal const uint LMEM_FIXED = 0x0000;
        internal const uint LMEM_ZEROINIT = 0x0040;

#if FEATURE_CORESYSTEM
        [SecurityCritical]
#else
#pragma warning disable 618    // Have not migrated to v4 transparency yet
        [SecurityCritical(SecurityCriticalScope.Everything)]
#pragma warning restore 618
#endif
        [SuppressUnmanagedCodeSecurity]
        internal static class UnsafeNativeMethods
        {
            /// <summary>
            ///     Open a crypto service provider, if a key container is specified KeyContainerPermission
            ///     should be demanded.
            /// </summary>
            [DllImport("advapi32", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
#if FEATURE_CORESYSTEM
            [SecurityCritical]
#endif
            public static extern bool CryptAcquireContext([Out] out SafeCspHandle phProv,
                                                          string? pszContainer,
                                                          string pszProvider,
                                                          ProviderType dwProvType,
                                                          CryptAcquireContextFlags dwFlags);

            /// <summary>
            ///     Get information about a hash algorithm, including the current value
            /// </summary>
            [DllImport("advapi32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
#if FEATURE_CORESYSTEM
            [SecurityCritical]
#endif
            public static extern bool CryptGetHashParam(SafeCapiHashHandle hHash,
                                                        HashParameter dwParam,
                                                        [Out, MarshalAs(UnmanagedType.LPArray)] byte[]? pbData,
                                                        [In, Out] ref int pdwDataLen,
                                                        int dwFlags);

            /// <summary>
            ///     Prepare a new hash algorithm for use
            /// </summary>
            [DllImport("advapi32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
#if FEATURE_CORESYSTEM
            [SecurityCritical]
#endif
            public static extern bool CryptCreateHash(SafeCspHandle hProv,
                                                      AlgorithmId Algid,
                                                      SafeCapiKeyHandle hKey,
                                                      int dwFlags,
                                                      [Out] out SafeCapiHashHandle phHash);

            /// <summary>
            ///     Add a block of data to a hash
            /// </summary>
            [DllImport("advapi32", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
#if FEATURE_CORESYSTEM
            [SecurityCritical]
#endif
            public static extern unsafe bool CryptHashData(SafeCapiHashHandle hHash,
                                                    byte* pbData,
                                                    int dwDataLen,
                                                    int dwFlags);
        }

        /// <summary>
        ///     Acquire a crypto service provider
        /// </summary>
        [System.Security.SecurityCritical]
        internal static SafeCspHandle AcquireCsp(string? keyContainer,
                                                 string providerName,
                                                 ProviderType providerType,
                                                 CryptAcquireContextFlags flags,
                                                 bool throwPlatformException)
        {
            if (!UnsafeNativeMethods.CryptAcquireContext(out SafeCspHandle? cspHandle,
                                                         keyContainer,
                                                         providerName,
                                                         providerType,
                                                           flags))
            {
                // If the platform doesn't have the specified CSP, we'll either get a ProviderTypeNotDefined
                // or a KeysetNotDefined error depending on the CAPI version.
                int error = Marshal.GetLastWin32Error();
                if (throwPlatformException && (error == (int)CapiNative.ErrorCode.ProviderTypeNotDefined ||
                                               error == (int)CapiNative.ErrorCode.KeysetNotDefined))
                {
                    throw new PlatformNotSupportedException(SR.Cryptography_PlatformNotSupported);
                }
                else
                {
                    throw new CryptographicException(error);
                }
            }

            return cspHandle;
        }

        /// <summary>
        ///     Get the value of a specific hash parameter
        /// </summary>
        [System.Security.SecurityCritical]
        internal static byte[] GetHashParameter(SafeCapiHashHandle hashHandle, CapiNative.HashParameter parameter)
        {
            Contract.Requires(hashHandle != null);
            Contract.Requires(CapiNative.HashParameter.AlgorithmId <= parameter && parameter <= CapiNative.HashParameter.HashSize);

            //
            // Determine the maximum size of the parameter and retrieve it
            //

            int parameterSize = 0;
            if (!CapiNative.UnsafeNativeMethods.CryptGetHashParam(hashHandle, parameter, null, ref parameterSize, 0))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            Debug.Assert(0 < parameterSize, "Invalid parameter size returned");
            byte[] parameterValue = new byte[parameterSize];
            if (!CapiNative.UnsafeNativeMethods.CryptGetHashParam(hashHandle, parameter, parameterValue, ref parameterSize, 0))
            {
                throw new CryptographicException(Marshal.GetLastWin32Error());
            }

            // CAPI may have asked for a larger buffer than it used, so only copy the used bytes
            if (parameterSize != parameterValue.Length)
            {
                byte[] realValue = new byte[parameterSize];
                Buffer.BlockCopy(parameterValue, 0, realValue, 0, parameterSize);
                parameterValue = realValue;
            }

            return parameterValue;
        }
    }
}
