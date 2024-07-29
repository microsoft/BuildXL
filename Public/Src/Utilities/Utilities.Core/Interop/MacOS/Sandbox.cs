// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using static BuildXL.Interop.Dispatch;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The Sandbox class offers interop calls for sandbox based tasks into the macOS sandbox interop library
    /// </summary>
    public static class Sandbox
    {
        public static unsafe int NormalizePathAndReturnHash(byte[] pPath, byte[] normalizedPath)
        {
            if (IsMacOS)
            {
                fixed (byte* outBuffer = &normalizedPath[0])
                {
                    return Impl_Mac.NormalizePathAndReturnHash(pPath, outBuffer, normalizedPath.Length);
                }
            }
            else
            {
                return Impl_Linux.NormalizePathAndReturnHash(pPath, normalizedPath);
            }
        }

        /// <summary>
        /// Callback the SandboxConnection uses to report any unrecoverable failure back to
        /// the scheduler (which, in response, should then terminate the build).
        /// </summary>
        /// <param name="status">Error code indicating what failure happened</param>
        /// <param name="description">Arbitrary description</param>
        public delegate void ManagedFailureCallback(int status, string description);
    }
}

#pragma warning restore CS1591