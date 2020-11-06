// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Chunk deduplication based on the Windows Server volume-level chunking algorithm.
    /// </summary>
    /// <remarks>
    /// Windows Server Deduplication: https://technet.microsoft.com/en-us/library/hh831602(v=ws.11).aspx
    /// More documentation: https://mseng.visualstudio.com/DefaultCollection/VSOnline/Artifact%20Services/_git/Content.VS?path=%2Fvscom%2Fintegrate%2Fapi%2Fdedup%2Fnode.md&amp;version=GBteams%2Fartifact%2Fversion2&amp;_a=contents
    /// </remarks>
    public static class Chunker
    {
        /// <summary>
        /// Chunks the buffer, calling back when chunks complete.
        /// </summary>
        public static void PushBuffer<T>(this T session, ArraySegment<byte> bytes)
            where T : IChunkerSession
        {
            Contract.Requires(bytes.Array != null);
            session.PushBuffer(bytes.Array, bytes.Offset, bytes.Count);
        }

        /// <summary>
        /// Creates a chunker appropriate to the runtime environment
        /// </summary>
        public static IChunker Create(ChunkerConfiguration configuration)
        {
            // Enforcing check earlier:
            // Use COMchunker only IFF avgchunksize = 64K, Windows 64bit and the module is present.
            // See 'IsComChunkerSupported'.
            if (configuration.AvgChunkSize == ChunkerConfiguration.SupportedComChunkerConfiguration.AvgChunkSize &&
                IsComChunkerSupported &&
                ComChunkerLoadError.Value == null)
            {
                try
                {
                    return new ComChunker(configuration);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Some older versions of windows. Fall back to managed chunker.
                }
                catch (System.Threading.ThreadStateException)
                {
                    // May happen in tests, when the thread apartment is not configured correctly. Fall back to managed chunker in this case as well.
                }
            }

            return new ManagedChunker(configuration);
        }

        /// <summary>
        /// Returns whether or not this environment supports chunking via the COM library
        /// </summary>
        public static readonly bool IsComChunkerSupported =
            (Environment.GetEnvironmentVariable("BUILDXL_TEST_FORCE_MANAGED_CHUNKER") != "1") && // TODO: Get rid of COM Chunker.
            (IntPtr.Size == 8) && 
#if NET_FRAMEWORK
            true;
#else
            System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif

        /// <nodoc />
        public static Lazy<Exception?> ComChunkerLoadError = new Lazy<Exception?>(() =>
        {
            try
            {
                var chunker = new ComChunker(ChunkerConfiguration.SupportedComChunkerConfiguration);
                using var session = chunker.BeginChunking(chunk => { });
                var content = new byte[1024 * 1024 + 1];
                session.PushBuffer(content, 0, content.Length);
                return null;
            }
            catch (Exception e)
            {
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                return e;
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }
        });
    }
}
