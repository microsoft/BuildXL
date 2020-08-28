using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>Implements SHA512 by PInvoking OpenSSL. This is almost 2x faster than the built-in SHA512.</summary>
    public class OpenSSL_SHA512 : IDisposable
    {
        private readonly IntPtr _ctx = Marshal.AllocHGlobal(216);   // sizeof(SHA512_CTX) in OpenSSL
        private readonly IntPtr _result = Marshal.AllocHGlobal(64); // SHA512_DIGEST_LENGTH

        /// <summary>(Re)initialize for a new hash. Must be called before first HashBuffer.</summary>
        public void Initialize() => CheckReturn(NativeMethods.SHA512_Init(_ctx), "SHA512_Init");

        /// <summary>Hash the contents of the specified buffer into the digest.</summary>
        public unsafe void HashBuffer(byte[] buffer, int offset, int count)
        {
            fixed (byte* buf = buffer)
            {
                CheckReturn(NativeMethods.SHA512_Update(_ctx, buf + offset, count), "SHA512_Update");
            }
        }

        /// <summary>Finalize and return the hash.</summary>
        /// <param name="digestTruncateLength">Truncate the returned digest to this length.</param>
        public byte[] Finalize(byte digestTruncateLength)
        {
            if (digestTruncateLength <= 0 || digestTruncateLength > 64)
            {
                throw new ArgumentException("must be >0 && <= 64", nameof(digestTruncateLength));
            }

            CheckReturn(NativeMethods.SHA512_Final(_result, _ctx), "SHA512_Final");

            byte[] hash = new byte[digestTruncateLength];
            Marshal.Copy(_result, hash, 0, hash.Length);
            return hash;
        }

        private static void CheckReturn(int returnValue, string api)
        {
            if (returnValue != 1)
            {
                throw new InvalidOperationException($"OpenSSL failed: {api}");
            }
        }

        /// <summary>Disposes unmanaged buffers.</summary>
        public void Dispose()
        {
            Marshal.FreeHGlobal(_ctx);
            Marshal.FreeHGlobal(_result);
        }

        /// <summary>OpenSSL PInvoke methods.</summary>
        private class NativeMethods
        {
            [DllImport("libcrypto", CallingConvention = CallingConvention.Cdecl)]
            public extern static int SHA512_Init(IntPtr ctx);

            [DllImport("libcrypto", CallingConvention = CallingConvention.Cdecl)]
            public unsafe extern static int SHA512_Update(IntPtr ctx, byte* data, long len);

            [DllImport("libcrypto", CallingConvention = CallingConvention.Cdecl)]
            public extern static int SHA512_Final(IntPtr result, IntPtr ctx);
        }
    }
}
