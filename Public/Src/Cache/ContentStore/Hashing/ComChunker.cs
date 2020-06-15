// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.DataDeduplication.Interop;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// Chunk deduplication based on the Windows Server volume-level chunking algorithm.
    /// </summary>
    /// <remarks>
    /// Windows Server Deduplication: https://technet.microsoft.com/en-us/library/hh831602(v=ws.11).aspx
    /// </remarks>
    public sealed class ComChunker : IChunker
    {
        internal const int SupportedAvgChunkSize = 64 * 1024;

        private readonly DeterministicChunker _inner;

        /// <summary>
        /// Creates a chunker that uses the Windows native COM library
        /// </summary>
        /// <param name="configuration"></param>
        public ComChunker(ChunkerConfiguration configuration)
        {
            _inner = new DeterministicChunker(
                configuration,
                new ComChunkerNonDeterministic(configuration));
        }

        /// <inheritdoc/>
        public ChunkerConfiguration Configuration => _inner.Configuration;

        /// <inheritdoc/>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            return _inner.BeginChunking(chunkCallback);
        }

        /// <inheritdoc/>
        public Pool<byte[]>.PoolHandle GetBufferFromPool()
        {
            return _inner.GetBufferFromPool();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _inner.Dispose();
        }
    }

    internal class ComChunkerNonDeterministic : INonDeterministicChunker
    {
        private static readonly Guid IteratorComGuid = new Guid("90B584D3-72AA-400F-9767-CAD866A5A2D8");
        private readonly IDedupIterateChunksHash32 _chunkHashIterator;
        private IDedupChunkLibrary _chunkLibrary;

        /// <summary>
        /// Initializes a new instance of the <see cref="Chunker"/> class.
        /// </summary>
        public ComChunkerNonDeterministic(ChunkerConfiguration config)
        {
            Contract.Assert(
                Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA,
                "Thread must be in MTA ApartmentState");

            Contract.Assert(
                config.AvgChunkSize == ComChunker.SupportedAvgChunkSize,
                "ComChunker only supports average chunk size of 64KB"
            );
                
            _chunkLibrary = NativeMethods.CreateChunkLibrary();
            _chunkLibrary.InitializeForPushBuffers();

            Action[] configures = new Action[]
            {
                () =>
                {
                    var max = (object)(config.MaxChunkSize);
                    _chunkLibrary.SetParameter(NativeMethods.DEDUP_PT_MaxChunkSizeBytes, ref max);
                },
                () =>
                {
                    // THIS IS NOT HONORED 
                    var avg = (object)(config.AvgChunkSize);
                    _chunkLibrary.SetParameter(NativeMethods.DEDUP_PT_AvgChunkSizeBytes, ref avg);
                },
                () =>
                {
                    var min = (object)(config.MinChunkSize);
                    _chunkLibrary.SetParameter(NativeMethods.DEDUP_PT_MinChunkSizeBytes, ref min);
                },
            };

            // The order in which these need to be set depends on whether we are going bigger or smaller
            // than the default.
            if (config.MaxChunkSize < ChunkerConfiguration.Default.MaxChunkSize)
            {
                Array.Reverse(configures);
            }

            foreach (Action configure in configures)
            {
                configure();
            }

            object chunksEnum;
            _chunkLibrary.StartChunking(IteratorComGuid, out chunksEnum);
            _chunkHashIterator = (IDedupIterateChunksHash32)chunksEnum;
        }

        /// <inheritdoc/>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            return new Session(this, chunkCallback);
        }

        /// <summary>
        /// Chunks the buffer, calling back when chunks complete.
        /// </summary>
        private unsafe void PushBuffer(byte[] buffer, int startOffset, int count, Action<ChunkInfo> chunkCallback)
        {
            if (count == 0)
            {
                return;
            }

            if (count < 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (startOffset < 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (startOffset + count > buffer.Length)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (byte* ptr = &buffer[startOffset])
            {
                _chunkHashIterator.PushBuffer(ptr, (uint)count);
            }

            ProcessChunks(chunkCallback);
        }

        /// <summary>
        /// Informs the chunker that all buffers have been pushed.  Calls back any remaining chunks.
        /// </summary>
        private unsafe void DonePushing(Action<ChunkInfo> chunkCallback)
        {
            _chunkHashIterator.Drain();
            ProcessChunks(chunkCallback);
            _chunkHashIterator.Reset();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_chunkLibrary != null)
            {
                _chunkLibrary.Uninitialize();
                // This may look wrong, but the invariant for the fully functioning instance is more important:
                // and for a fully initialized and not destroyed instance _chunkLibrary is not null.
                _chunkLibrary = null!;
            }
        }

        private void ProcessChunks(Action<ChunkInfo> chunkCallback)
        {
            uint ulFetchedChunks;
            do
            {
                DedupChunkInfoHash32[] hashInfos = new DedupChunkInfoHash32[4];
                int retVal = _chunkHashIterator.Next((uint)hashInfos.Length, hashInfos, out ulFetchedChunks);

                for (uint i = 0; i < ulFetchedChunks; i++)
                {
                    chunkCallback(new ChunkInfo(
                        hashInfos[i].StreamOffset,
                        (uint)hashInfos[i].DataSize,
                        hashInfos[i].Hash));
                }

                if (retVal == 0)
                {
                    // Continue getting more chunks.
                }
                else if (retVal == 1 || retVal == NativeMethods.DDP_E_MORE_BUFFERS)
                {
                    break;
                }
                else
                {
                    throw new Win32Exception($"{retVal}");
                }
            }
            while (ulFetchedChunks > 0);
        }

        /// <inheritdoc/>
        private sealed class Session : IChunkerSession, IDisposable
        {
            private readonly ComChunkerNonDeterministic _chunker;
            private readonly Action<ChunkInfo> _chunkCallback;

            /// <inheritdoc/>
            public Session(ComChunkerNonDeterministic chunker, Action<ChunkInfo> chunkCallback)
            {
                _chunker = chunker;
                _chunkCallback = chunkCallback;
            }

            /// <inheritdoc/>
            public void PushBuffer(byte[] buffer, int startOffset, int count)
            {
                _chunker.PushBuffer(buffer, startOffset, count, _chunkCallback);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                _chunker.DonePushing(_chunkCallback);
            }
        }

        private static class NativeMethods
        {
#pragma warning disable SA1310 // Field names must not contain underscore
            // ReSharper disable once InconsistentNaming
            public const int DDP_E_MORE_BUFFERS = unchecked((int)0x8056531b);

            public const int DEDUP_PT_MinChunkSizeBytes = 1;
            public const int DEDUP_PT_MaxChunkSizeBytes = 2;
            public const int DEDUP_PT_AvgChunkSizeBytes = 3;
            public const int DEDUP_PT_InvariantChunking = 4;
            public const int DEDUP_PT_DisableStrongHashComputation = 5;
#pragma warning restore SA1310 // Field names must not contain underscore

            private static IntPtr LoadNativeLibrary(string libraryName)
            {
                // First, try to load from the system directory or the folder this library is in.
                // See https://docs.microsoft.com/en-us/windows/win32/dlls/dynamic-link-library-search-order
                IntPtr hLib = LoadLibrary(libraryName);
                if (hLib == IntPtr.Zero)
                {
                    // If not there, we carry a copy with us in the x64 folder.
                    hLib = LoadLibrary($"x64\\{libraryName}");
                    if (hLib == IntPtr.Zero)
                    {
                        throw new Win32Exception($"Could not load {libraryName}' on {Environment.OSVersion}: {Marshal.GetLastWin32Error()}");
                    }
                }

                return hLib;
            }

            private static readonly Lazy<IClassFactory> ClassFactory = new Lazy<IClassFactory>(
                () =>
                {
                    if (!Environment.Is64BitOperatingSystem)
                    {
                        throw new NotSupportedException("Azure DevOps chunker requires a 64-bit operating system.");
                    }

                    if (!Environment.Is64BitProcess)
                    {
                        throw new NotSupportedException("Azure DevOps chunker must be run as a 64-bit process.");
                    }

                    IntPtr hDdpTraceLib = LoadNativeLibrary("ddptrace.dll");
                    IntPtr hDdpChunkLib = LoadNativeLibrary("ddpchunk.dll");

                    IntPtr pDllGetClassObject = GetProcAddress(hDdpChunkLib, "DllGetClassObject");
                    if (pDllGetClassObject == IntPtr.Zero)
                    {
                        throw new Win32Exception($"Could not find 'DllGetClassObject' on {Environment.OSVersion}: {Marshal.GetLastWin32Error()}");
                    }

                    var dllGetClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(pDllGetClassObject);

                    object callFactoryObj;
                    int hresult = dllGetClassObject(
                        ChunkLibraryClsId,
                        typeof(IClassFactory).GUID,
                        out callFactoryObj);
                    if (hresult < 0)
                    {
                        throw new Win32Exception($"Failed to get class factory for '{ChunkLibraryClsId}' on {Environment.OSVersion}: {hresult}");
                    }

                    return (IClassFactory)callFactoryObj;
                },
                LazyThreadSafetyMode.ExecutionAndPublication);

            public static IDedupChunkLibrary CreateChunkLibrary()
            {
                Guid copyChunkLibraryComGuid = ChunkLibraryComGuid;
                object dedupLibraryObj = ClassFactory.Value.CreateInstance(
                    null,
                    ref copyChunkLibraryComGuid);

                return (IDedupChunkLibrary)dedupLibraryObj;
            }

            private static readonly Guid ChunkLibraryComGuid = new Guid("BB5144D7-2720-4DCC-8777-78597416EC23");
            private static readonly Guid ChunkLibraryClsId = new Guid("BF6E2DC4-960B-49C4-BE73-A2334AAB6D69");

            [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
            private static extern IntPtr LoadLibrary([MarshalAs(UnmanagedType.LPWStr)] string lpFileName);

            [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
            private static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

            [UnmanagedFunctionPointer(CallingConvention.StdCall)]
            private delegate int DllGetClassObjectDelegate(
                [In] [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
                [In] [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
                [Out, MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 1)] out object ppv);

            /// <summary>
            /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms694364(v=vs.85).aspx
            /// </summary>
            [ComImport]
            [Guid("00000001-0000-0000-C000-000000000046")]
            [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
            private interface IClassFactory
            {
                /// <summary>
                /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms682215(v=vs.85).aspx
                /// </summary>
                [return: MarshalAs(UnmanagedType.IUnknown, IidParameterIndex = 1)]
                object CreateInstance(
                    [MarshalAs(UnmanagedType.IUnknown)] object? pUnkOuter,
                    [In] ref Guid riid);

                /// <summary>
                /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms682332(v=vs.85).aspx
                /// </summary>
                // ReSharper disable once UnusedMember.Global
                void LockServer(
                    [MarshalAs(UnmanagedType.Bool)] bool fLock);
            }
        }
    }
}

namespace Microsoft.DataDeduplication.Interop
{
    /// <nodoc />
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("BB5144D7-2720-4DCC-8777-78597416EC23")]
    public interface IDedupChunkLibrary
    {
        /// <nodoc />
        void InitializeForPushBuffers();

        /// <nodoc />
        void Uninitialize();

        /// <nodoc />
        void SetParameter(
            uint paramType,
            ref object paramVariant);

        /// <nodoc />
        void StartChunking(
            Guid iteratorInterfaceID,
            [MarshalAs(UnmanagedType.IUnknown)]
            out object chunkEnumerator);
    }
    
    /// <nodoc />
    [StructLayout(LayoutKind.Sequential)]
    public struct DedupChunkInfoHash32
    {
        /// <nodoc />
        public uint Flags;

        /// <nodoc />
        public ulong StreamOffset;

        /// <nodoc />
        public ulong DataSize;

        /// <nodoc />
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Hash; // 32-byte chunk hash value
    }

    /// <nodoc />
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("90B584D3-72AA-400F-9767-CAD866A5A2D8")]
    unsafe public interface IDedupIterateChunksHash32
    {
        /// <nodoc />
        void PushBuffer(
            byte* data,
            uint dataLength);

        /// <nodoc />
        [PreserveSig()]
        int Next(
            uint maxChunks,
            [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] 
            DedupChunkInfoHash32[] hashes,
            out uint chunkCount);

        /// <nodoc />
        void Drain();

        /// <nodoc />
        void Reset();
    }
}
