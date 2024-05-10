// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Text;

using static BuildXL.Interop.Dispatch;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The Sandbox class offers interop calls for sandbox based tasks into the macOS sandbox interop library
    /// </summary>
    public static class Sandbox
    {
        public static readonly int ReportQueueSuccessCode = 0x1000;
        public static readonly int SandboxSuccess = 0x0;

        private static readonly Encoding s_accessReportStringEncoding = Encoding.UTF8;

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


        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReportStatistics
        {
            public ulong CreationTime;
            public ulong EnqueueTime;
            public ulong DequeueTime;
        }

        /// <remarks>
        /// CODESYNC: Public/Src/Sandbox/MacOs/Sandbox/Src/BuildXLSandboxShared.hpp
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReport
        {
            /// <summary>Reported file operation.</summary>
            public FileOperation Operation;

            /// <summary>Process ID of the process making the accesses</summary>
            public int Pid;

            /// <summary>Process ID of the root pip process</summary>
            public int RootPid;

            public uint RequestedAccess;
            public uint Status;
            public uint ExplicitLogging;
            public uint Error;
            public long PipId;

            /// <summary>
            /// Corresponds to a <c>union { char path[MAXPATHLEN]; PipCompletionStats pipStats; }</c> C type.
            /// Use <see cref="DecodePath"/> method to decode this into either a path string
            /// </summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=Constants.MaxPathLength)]
            public byte[] PathOrPipStats;   // TODO [maly]: we only use this for path, so we can optimize the pipstats thing away (I think!)

            public AccessReportStatistics Statistics;

            public uint IsDirectory;

            /// <summary>
            /// This report was sent before the sandbox was fully initialized.
            /// </summary>
            public uint UnexpectedReport;

            public string DecodeOperation() => Operation.GetName();

            /// <summary>
            /// Encodes a given string into a byte array of a given size.
            /// </summary>
            public static byte[] EncodePath(string path, int bufferLength = Constants.MaxPathLength)
            {
                byte[] result = new byte[bufferLength];
                s_accessReportStringEncoding.GetBytes(path, charIndex: 0, charCount: path.Length, bytes: result, byteIndex: 0);
                return result;
            }

            /// <summary>
            /// Interprets <see cref="PathOrPipStats"/> as a 0-terminated UTF8-encoded string.
            /// </summary>
            public string DecodePath()
            {
                Contract.Requires(PathOrPipStats != null);
                return s_accessReportStringEncoding.GetString(PathOrPipStats).TrimEnd('\0');
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