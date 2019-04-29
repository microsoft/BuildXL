// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Implementation of a generic CAPI hashing algorithm, concrete HashAlgorithm classes
    ///     implemented by CAPI can contain an instance of this class and delegate the work to it
    /// </summary>
    internal sealed class CapiHashAlgorithm : IDisposable
    {
        private readonly CapiNative.AlgorithmId m_algorithmId;
        private readonly SafeCspHandle m_cspHandle;
        private SafeCapiHashHandle m_hashHandle;
        
        /*
         * SafeCritical - we're not exposing out anything that we want to prevent untrusted code from getting at
         */
        public CapiHashAlgorithm(
            string provider,
            CapiNative.ProviderType providerType,
            CapiNative.AlgorithmId algorithm)
        {
            Contract.Requires(!string.IsNullOrEmpty(provider));
            Contract.Requires((CapiNative.AlgorithmClass)((uint)algorithm & (uint)CapiNative.AlgorithmClass.Hash) == CapiNative.AlgorithmClass.Hash);
            Contract.Ensures(m_cspHandle != null && !m_cspHandle.IsInvalid && !m_cspHandle.IsClosed);
            Contract.Ensures(m_hashHandle != null && !m_hashHandle.IsInvalid && !m_hashHandle.IsClosed);

            m_algorithmId = algorithm;
            m_cspHandle = CapiNative.AcquireCsp(null,
                                                provider,
                                                providerType,
                                                CapiNative.CryptAcquireContextFlags.VerifyContext,
                                                true);
            Initialize();
        }
        
        public void Dispose()
        {
            m_hashHandle?.Dispose();

            m_cspHandle?.Dispose();
        }

        /// <summary>
        ///     Reset the hash algorithm to begin hashing a new set of data
        /// </summary>
        public void Initialize()
        {
            Contract.Ensures(m_hashHandle != null && !m_hashHandle.IsInvalid && !m_hashHandle.IsClosed);
            Contract.Assert(m_cspHandle != null);

            // Try to create a new hash algorithm to use
            SafeCapiHashHandle newHashAlgorithm = null;

            RuntimeHelpers.PrepareConstrainedRegions();
            try
            {
                if (!CapiNative.UnsafeNativeMethods.CryptCreateHash(m_cspHandle,
                                                                    m_algorithmId,
                                                                    SafeCapiKeyHandle.InvalidHandle,
                                                                    0,
                                                                    out newHashAlgorithm))
                {
                    // BadAlgorithmId means that this CSP does not support the specified algorithm, which means
                    // that we're on a platform that does not support the given hash function.
                    int error = Marshal.GetLastWin32Error();
                    if (error == (int)CapiNative.ErrorCode.BadAlgorithmId)
                    {
                        throw new PlatformNotSupportedException(SR.Cryptography_PlatformNotSupported);
                    }
                    else
                    {
                        throw new CryptographicException(error);
                    }
                }
            }
            finally
            {
                if (newHashAlgorithm != null && !newHashAlgorithm.IsInvalid)
                {
                    newHashAlgorithm.SetParentCsp(m_cspHandle);
                }
            }

            // If we created a new algorithm, dispose of the old one and use the new one
            Debug.Assert(newHashAlgorithm != null, "newHashAlgorithm != null");
            if (m_hashHandle != null)
            {
                m_hashHandle.Dispose();
            }

            m_hashHandle = newHashAlgorithm;
        }

        /// <summary>
        ///     Hash a block of data
        /// </summary>
        public void HashCore(byte[] array, int ibStart, int cbSize)
        {
            Contract.Assert(m_hashHandle != null);

            if (array == null)
            {
                throw new ArgumentNullException("array");
            }

            if (ibStart < 0 || ibStart > array.Length - cbSize)
            {
                throw new ArgumentOutOfRangeException("ibStart");
            }

            if (cbSize < 0 || cbSize > array.Length)
            {
                throw new ArgumentOutOfRangeException("cbSize");
            }

            unsafe
            {
                fixed (byte* dataPtr = array)
                {
                    byte* arrayPtr = dataPtr + ibStart;
                    if (!CapiNative.UnsafeNativeMethods.CryptHashData(m_hashHandle, arrayPtr, cbSize, 0))
                    {
                        throw new CryptographicException(Marshal.GetLastWin32Error());
                    }
                }
            }
        }

        /// <summary>
        ///     Complete the hash, returning its value
        /// </summary>
        public byte[] HashFinal()
        {
            Contract.Ensures(Contract.Result<byte[]>() != null);
            Contract.Assert(m_hashHandle != null);

            return CapiNative.GetHashParameter(m_hashHandle, CapiNative.HashParameter.HashValue);
        }
    }
}
