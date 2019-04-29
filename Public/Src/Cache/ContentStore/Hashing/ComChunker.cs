// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.ComponentModel;
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
    public class ComChunker : IChunker, IDisposable
    {
        /// <summary>
        /// To get deterministic chunks out of the chunker, only give it buffers of at least 256KB, unless EOF.
        /// Cosmin Rusu recommends larger buffers for performance, so going with 1MB.
        /// </summary>
        /// TODO: use object pool here. Bug #1331905
        public const uint MinPushBufferSize = 1024 * 1024;

        private static readonly Guid IteratorComGuid = new Guid("90B584D3-72AA-400F-9767-CAD866A5A2D8");
        private readonly byte[] _pushBuffer = new byte[MinPushBufferSize];
        private readonly IDedupIterateChunksHash32 _chunkHashIterator;
        private IDedupChunkLibrary _chunkLibrary;
        private long _totalBytes;
        private uint _bytesInPushBuffer;

        /// <summary>
        /// Gets total number of bytes chunked.
        /// </summary>
        public long TotalBytes => _totalBytes;

        /// <summary>
        /// Initializes a new instance of the <see cref="Chunker"/> class.
        /// </summary>
        public ComChunker()
        {
            _bytesInPushBuffer = 0;
            _totalBytes = 0;
            _chunkLibrary = NativeMethods.CreateChunkLibrary();
            _chunkLibrary.InitializeForPushBuffers();

            object chunksEnum;
            _chunkLibrary.StartChunking(IteratorComGuid, out chunksEnum);
            _chunkHashIterator = (IDedupIterateChunksHash32)chunksEnum;
        }

        /// <summary>
        /// Creates a session for chunking a stream from a series of buffers.
        /// </summary>
        public IChunkerSession BeginChunking(Action<ChunkInfo> chunkCallback)
        {
            Reset();
            return new Session(this, chunkCallback);
        }

        /// <summary>
        /// Reinitializes this instance for reuse.
        /// </summary>
        private void Reset()
        {
            _totalBytes = 0;
            _chunkHashIterator.Reset();
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

            if (startOffset < 0 || count < 0)
            {
                throw new IndexOutOfRangeException();
            }

            if (startOffset + count > buffer.Length)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (byte* incomingBuffer = &buffer[startOffset])
            fixed (byte* pushBuffer = _pushBuffer)
            {
                byte* incomingBufferHead = incomingBuffer;
                byte* pushBufferTail = pushBuffer + _bytesInPushBuffer;
                while (count > 0)
                {
                    while (count > 0 && _bytesInPushBuffer < MinPushBufferSize)
                    {
                        *pushBufferTail = *incomingBufferHead;
                        incomingBufferHead++;
                        pushBufferTail++;
                        _bytesInPushBuffer++;
                        count--;
                    }

                    if (_bytesInPushBuffer == MinPushBufferSize)
                    {
                        _chunkHashIterator.PushBuffer(_pushBuffer, _bytesInPushBuffer);
                        _totalBytes += _bytesInPushBuffer;

                        _bytesInPushBuffer = 0;
                        pushBufferTail = pushBuffer;

                        ProcessChunks(chunkCallback);
                    }
                }
            }
        }

        /// <summary>
        /// Informs the chunker that all buffers have been pushed.  Calls back any remaining chunks.
        /// </summary>
        private unsafe void DonePushing(Action<ChunkInfo> chunkCallback)
        {
            if (_bytesInPushBuffer > 0)
            {
                _chunkHashIterator.PushBuffer(_pushBuffer, _bytesInPushBuffer);
                _totalBytes += _bytesInPushBuffer;

                _bytesInPushBuffer = 0;

                ProcessChunks(chunkCallback);
            }

            if (TotalBytes == 0)
            {
                return;
            }

            _chunkHashIterator.Drain();
            ProcessChunks(chunkCallback);
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        public void Dispose()
        {
            if (_chunkLibrary == null)
            {
                throw new ObjectDisposedException(nameof(_chunkLibrary));
            }

            _chunkLibrary.Uninitialize();
            _chunkLibrary = null;
        }

        private void ProcessChunks(Action<ChunkInfo> chunkCallback)
        {
            if (TotalBytes == 0)
            {
                return;
            }

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

        /// <summary>
        /// A session for chunking a stream from a series of buffers
        /// </summary>
        public readonly struct Session : IChunkerSession, IDisposable
        {
            private readonly ComChunker _chunker;
            private readonly Action<ChunkInfo> _chunkCallback;

            /// <summary>
            /// Initializes a new instance of the <see cref="Session"/> struct.
            /// </summary>
            public Session(ComChunker chunker, Action<ChunkInfo> chunkCallback)
            {
                _chunker = chunker;
                _chunkCallback = chunkCallback;
            }

            /// <summary>
            /// Chunks the buffer, calling back when chunks complete.
            /// </summary>
            public void PushBuffer(byte[] buffer, int startOffset, int count)
            {
                _chunker.PushBuffer(buffer, startOffset, count, _chunkCallback);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                try
                {
                    _chunker.DonePushing(_chunkCallback);
                }
                catch (COMException e) when ((uint)e.ErrorCode == 0x80565319)
                {
                    // Maybe in in an "invalid state"
                }
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                throw new InvalidOperationException();
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                throw new InvalidOperationException();
            }

            /// <nodoc />
            public static bool operator ==(Session left, Session right)
            {
                throw new InvalidOperationException();
            }

            /// <nodoc />
            public static bool operator !=(Session left, Session right)
            {
                throw new InvalidOperationException();
            }
        }

        private static class NativeMethods
        {
#pragma warning disable SA1310 // Field names must not contain underscore
            // ReSharper disable once InconsistentNaming
            public const int DDP_E_MORE_BUFFERS = unchecked((int)0x8056531b);
#pragma warning restore SA1310 // Field names must not contain underscore

            private static readonly Lazy<IClassFactory> ClassFactory = new Lazy<IClassFactory>(
                () =>
                {
                    IntPtr hDdpTraceLib = LoadLibrary("ddptrace.dll");
                    if (hDdpTraceLib == IntPtr.Zero)
                    {
                        throw new Win32Exception($"Could not load 'ddptrace.dll': {Marshal.GetLastWin32Error()}");
                    }

                    IntPtr hDdpChunkLib = LoadLibrary("ddpchunk.dll");
                    if (hDdpChunkLib == IntPtr.Zero)
                    {
                        throw new Win32Exception($"Could not load 'ddpchunk.dll': {Marshal.GetLastWin32Error()}");
                    }

                    IntPtr pDllGetClassObject = GetProcAddress(hDdpChunkLib, "DllGetClassObject");
                    if (pDllGetClassObject == IntPtr.Zero)
                    {
                        throw new Win32Exception($"Could not find 'DllGetClassObject': {Marshal.GetLastWin32Error()}");
                    }

                    var dllGetClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectDelegate>(pDllGetClassObject);

                    object callFactoryObj;
                    int hresult = dllGetClassObject(
                        ChunkLibraryClsId,
                        typeof(IClassFactory).GUID,
                        out callFactoryObj);
                    if (hresult < 0)
                    {
                        throw new Win32Exception($"Failed to get class factory for '{ChunkLibraryClsId}': {hresult}");
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
                    [MarshalAs(UnmanagedType.IUnknown)] object pUnkOuter,
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
    public interface IDedupIterateChunksHash32
    {
        /// <nodoc />
        void PushBuffer(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)]
            byte[] data,
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
