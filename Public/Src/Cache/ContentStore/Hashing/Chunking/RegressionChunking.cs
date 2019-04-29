// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1000
#pragma warning disable SA1002
#pragma warning disable SA1003
#pragma warning disable SA1021
#pragma warning disable SA1023
#pragma warning disable SA1025
#pragma warning disable SA1028
#pragma warning disable SA1106
#pragma warning disable SA1107
#pragma warning disable SA1108
#pragma warning disable SA1119
#pragma warning disable SA1120
#pragma warning disable SA1131
#pragma warning disable SA1121 // Use built-in type alias
#pragma warning disable SA1210
#pragma warning disable SA1300 // Element must begin with upper-case letter
#pragma warning disable SA1303 // Const field names must begin with upper-case letter
#pragma warning disable SA1308 // Variable names must not be prefixed
#pragma warning disable SA1310 // Field names must not contain underscore
#pragma warning disable SA1312
#pragma warning disable SA1311 // Static readonly fields must begin with upper-case letter
#pragma warning disable SA1400 // Access modifier must be declared
#pragma warning disable SA1503
#pragma warning disable SA1507
#pragma warning disable SA1508
#pragma warning disable SA1512
#pragma warning disable SA1513
#pragma warning disable SA1515
#pragma warning disable SA1516
#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable SA1602

namespace BuildXL.Cache.ContentStore.Hashing.Chunking
{
    using BYTE = System.Byte;
    using DWORD = System.UInt32;
    using HashValueT = System.UInt64;
    using OffsetT = System.Int64;
    using size_t = System.UInt64;
    using ULONG = System.UInt64;

    //[GeneratedCode("Copied from Windows Sources", "1.0")]
    // Adapted from https://microsoft.visualstudio.com/OS/_git/os?path=%2Fservercommon%2Fbase%2Ffs%2Fdedup%2Fmodules%2Fchunk%2FRegressionChunking.cpp&version=GBofficial%2Frsmaster&_a=contents
    public sealed class RegressionChunking
    {
        const DWORD g_dwChunkingTruncateBits = 16;
        const DWORD g_dwChunkingHashMatchValue = 0x5555;
        const ULONG g_ulMinimumChunkSizeDefault = 32 * 1024;
        public const ULONG g_ulMaximumChunkSizeDefault = 128 * 1024;
        const ULONG g_ulAverageChunkSizeDefault = 64 * 1024;

        // Sliding window size (in bytes) for Rabin hashing
        public const size_t m_nWindowSize = 16;

        // Number of past offset slots to be remembered for regression
        const size_t m_nRegressSize = 4;

        // Polynomial values
        const int PolynomialsLength = 256;
        static readonly HashValueT[] g_arrPolynomialsTD = Rabin64Table.g_arrPolynomialsTD;
        static readonly HashValueT[] g_arrPolynomialsTU = Rabin64Table.g_arrPolynomialsTU;

        // Parameters
        private size_t m_nMinChunkSize => g_ulMinimumChunkSizeDefault;
        private size_t m_nMaxChunkSize => g_ulMaximumChunkSizeDefault;
        private size_t m_nAverageChunkSize => g_ulAverageChunkSizeDefault;

        private DWORD m_dwInitialChunkingTruncateBits => g_dwChunkingTruncateBits;
        private DWORD m_dwInitialChunkingHashMatchValue => g_dwChunkingHashMatchValue;

        private readonly DWORD m_dwSmallestChunkingTruncateMask;
        private readonly DWORD m_dwSmallestChunkingHashMatchValue;

        // State maintained across multiple FindRabinChunkBoundariesInternal() calls
        private HashValueT m_hash;               // Last hash value
        private readonly BYTE[] m_history;                // Last bytes from the previous chunk. Used to reinitialize the state machine for chunking (size = window size)

        private readonly OffsetT[] m_regressChunkLen;
        private OffsetT m_lastNonZeroChunkLen;
        private OffsetT m_numZeroRun;                       // Size of continuous zeros after last chunk. 
                                                    // >= 0, a run is counted (# of consecutive zeros)
                                                    // <  0, a run has been interrupted, i.e., encounter at least one none zero values. 

        // Regress hash values
        private readonly DWORD[] m_arrRegressChunkingTruncateMask;             // Array of hash masks for the regression match
        private readonly DWORD[] m_arrRegressChunkingHashMatchValue;           // Array of hash values for the regression matche

        private size_t previouslyProcessedBytesAcrossCalls;
        private size_t lastChunkAbsoluteOffsetAcrossCalls;

        private readonly List<DedupBasicChunkInfo> outOffsetsVector = new List<DedupBasicChunkInfo>();
        private readonly Action<DedupBasicChunkInfo> chunkCallback;

        public IReadOnlyList<DedupBasicChunkInfo> Chunks => outOffsetsVector;

        public RegressionChunking(Action<DedupBasicChunkInfo> chunkCallback)
        {
            this.chunkCallback = chunkCallback;

            m_history = new BYTE[m_nWindowSize];
            m_regressChunkLen = new OffsetT[m_nRegressSize];
            m_arrRegressChunkingTruncateMask = new DWORD[m_nRegressSize];
            m_arrRegressChunkingHashMatchValue = new DWORD[m_nRegressSize];

            m_dwSmallestChunkingTruncateMask = 0;
            m_dwSmallestChunkingHashMatchValue = 0;
            previouslyProcessedBytesAcrossCalls = 0;
            lastChunkAbsoluteOffsetAcrossCalls = 0;

            m_hash = 0;
            for (int i = 0; i < m_regressChunkLen.Length; i++)
            {
                m_regressChunkLen[i] = -1;
            }

            m_numZeroRun = 0;
            m_lastNonZeroChunkLen = -1;

            // Initialize
            // The maximum value of N in the comparison above (default = 16 bits)
            DWORD dwChunkingTruncateMask = (DWORD)((1 << (int)m_dwInitialChunkingTruncateBits) - 1);

            // This is the value we are using for chunking: if the least significant N bytes from the Rabin hash are equal to this value, we declare a "cut" (where N depends on the context)
            DWORD dwChunkingHashMatchValue = m_dwInitialChunkingHashMatchValue & dwChunkingTruncateMask;

            // Initialize a set of mask & match value, each has one bit less than previous mask. 
            for (size_t regressIndex = 0; regressIndex < m_nRegressSize; regressIndex++)
            {
                m_arrRegressChunkingTruncateMask[regressIndex] = dwChunkingTruncateMask;

                m_arrRegressChunkingHashMatchValue[regressIndex] = dwChunkingHashMatchValue;

                dwChunkingTruncateMask >>= 1;
                dwChunkingHashMatchValue &= dwChunkingTruncateMask;
            }

            m_dwSmallestChunkingTruncateMask = dwChunkingTruncateMask;
            m_dwSmallestChunkingHashMatchValue = dwChunkingHashMatchValue;
        }

        public void PushBuffer(
            BYTE[] buffer
        )
        {
            PushBuffer(new ArraySegment<BYTE>(buffer));
        }

        public void PushBuffer(
            ArraySegment<BYTE> buffer
        )
        {
            size_t size = (size_t)buffer.Count;
            bool bNoMoreData = false;

            if (size == 0)
            {
                return;
            }
            else if (size < m_nWindowSize)
            {
                previouslyProcessedBytesAcrossCalls += size;
                return;
            }

            unsafe
            {
                fixed (byte* p = &buffer.Array[buffer.Offset]) // byte * p = buffer.GetContinuousBuffer(iSizeDone, size);
                {
                    FindRabinChunkBoundariesInternal(
                        p,
                        size,
                        bNoMoreData,
                        ref previouslyProcessedBytesAcrossCalls,
                        ref lastChunkAbsoluteOffsetAcrossCalls);
                }
            }
        }

        public void Complete()
        {
            AddChunkInfo(
                new DedupBasicChunkInfo(
                    lastChunkAbsoluteOffsetAcrossCalls,
                    previouslyProcessedBytesAcrossCalls,
                    DedupChunkCutType.DDP_CCT_EndReached));
        }

        private static void NT_ASSERT(bool expression)
        {
            if(!expression)
            {
                throw new InvalidOperationException();
            }
        }

        private static unsafe void DDP_BUFFER_RANGE_ASSERT(
            byte * pTestedPointer,
            byte * pStartBuffer,
            byte * pEndBuffer)
        {
            NT_ASSERT(pTestedPointer != null);
            NT_ASSERT(pTestedPointer >= pStartBuffer);
            NT_ASSERT(pTestedPointer < pEndBuffer);
        }

        private static unsafe size_t ARRAYSIZE<T>(T[] array)
        {
            return (size_t)array.Length;
        }

        private static unsafe void DDP_ASSERT_VALID_ARRAY_INDEX(long nArrayIndex, byte[] arrValues)
        {
            NT_ASSERT((OffsetT)(nArrayIndex) < (OffsetT)(ARRAYSIZE(arrValues)));
            NT_ASSERT((OffsetT)(nArrayIndex) >= 0);
        }

        private static unsafe void DDP_ASSERT_VALID_ARRAY_INDEX(ulong nArrayIndex, long[] arrValues)
        {
            NT_ASSERT((OffsetT)(nArrayIndex) < (OffsetT)(ARRAYSIZE(arrValues)));
            NT_ASSERT((OffsetT)(nArrayIndex) >= 0);
        }

        private void AddChunkInfo(DedupBasicChunkInfo chunkInfo)
        {
            outOffsetsVector.Add(chunkInfo);
            chunkCallback(chunkInfo);
        }

        private unsafe void FindRabinChunkBoundariesInternal(
            byte* pStartBuffer,                        // Pointer to the BYTE buffer of data to be chunked in a sequence of FindRabinChunkBoundariesInternal() calls
            size_t cbLen,                                            // Length of the data to be chunked in a sequence of FindRabinChunkBoundariesInternal() calls
            bool bNoMoreData,                                        // If TRUE, this is the last call in the sequence of FindRabinChunkBoundariesInternal() calls on this data
            ref size_t previouslyProcessedBytesParam,                     // Temporary state between calls FindRabinChunkBoundariesInternal(). Amount of previously processed bytes since the last recorded chunk
            ref size_t lastChunkAbsoluteOffsetParam                       // Temporary state between calls FindRabinChunkBoundariesInternal(). Offset of the last inserted chunk, relative to the overall buffer in FindRabinChunkBoundaries()
        )
        {
            unchecked
            {
                NT_ASSERT(cbLen > 0);
                NT_ASSERT(pStartBuffer != null);
                NT_ASSERT(previouslyProcessedBytesParam <= m_nMaxChunkSize);

                //
                // Buffer validation support
                //

                // Used to define the end of buffer (the first byte beyond the addressable pStartBuffer)
                // Used only for DDP_ASSERT_VALID_XXX asserts
                byte* pEndBuffer = pStartBuffer + cbLen;
                NT_ASSERT(pStartBuffer < pEndBuffer);        // Ensure that we don't have arithmetic overrun

                Action<IntPtr> DDP_ASSERT_VALID_BUFFER_POINTER = (IntPtr pTestedPointer) =>
                {
                    DDP_BUFFER_RANGE_ASSERT((byte*)pTestedPointer, pStartBuffer, pEndBuffer);
                };

                Action<IntPtr> DDP_ASSERT_VALID_BUFFER_END = (IntPtr pTestedPointer) =>
                {
                    DDP_BUFFER_RANGE_ASSERT((byte*)pTestedPointer, pStartBuffer + 1, pEndBuffer + 1);
                };

                Action<IntPtr> DDP_ASSERT_VALID_START_POINTER = (IntPtr pTestedPointer) =>
                {
                    DDP_BUFFER_RANGE_ASSERT((byte*)pTestedPointer + m_nWindowSize, pStartBuffer, pEndBuffer + 1);
                };

                Action<IntPtr> DDP_ASSERT_VALID_END_POINTER = (IntPtr pTestedPointer) =>
                {
                    DDP_BUFFER_RANGE_ASSERT((byte*)pTestedPointer, pStartBuffer, pEndBuffer + 1);
                };

                Func<IntPtr, bool> DDP_IS_VALID_POINTER = (IntPtr pTestedPointer) =>
                    (((byte*)pTestedPointer >= pStartBuffer) && ((byte*)pTestedPointer < pEndBuffer));


                //
                // Local state variables (for this call)
                //

                // During chunking, this is the index where we start the "look-ahead". This offset is relative to the beginning of the current buffer
                // Note: this value should always be positive 
                OffsetT startOffset = 0;

                // Amount of bytes available in the buffer for chunking/analysis from startOffset to the end of the buffer. Equal or smaller than the size of the buffer
                // Note: this value should be always positive
                OffsetT remainingBytes = (OffsetT)cbLen;

                // Amount of previously processed bytes (since we recorded the last chunk) until the startOffset. Represents bytes from the previous call, in the initial chunking iteration
                size_t previouslyProcessedBytes = previouslyProcessedBytesParam;

                // Offset of the last inserted chunk relative to the file stream (i.e. to the beginning of the virtual buffer spanning the sequence of calls)
                size_t lastChunkAbsoluteOffset = lastChunkAbsoluteOffsetParam;

                // Holds the currently computed Rabin hash value over the current window. At the end of thus call, we will move this value back to m_hash
                HashValueT hash = m_hash;

                /*

                // Per-instance state members (passed across calls)

                LONGLONG m_numZeroRun -                      // Size of continuous zeros after last chunk up to the current offset. 
                                                             //   >= 0, a run is counted (# of consecutive zeros)
                                                             //   <  0, a run has been interrupted, i.e., encounter at least one none zero values. 
                HashValueT m_hash;                           // Last hash value
                BYTE m_history[m_nWindowSize];               // Last bytes from the previous chunk. Used to reinitialize the state machine for chunking (size = window size)

                OffsetT m_regressChunkLen[m_nRegressSize]; 
                OffsetT m_lastNonZeroChunkLen; 

                TODO:365262 - simplify the code by allocating a larger buffer and copying the data to ensure a continuous operating buffer

                */

                // smallest hash mask and match value
                DWORD dwSmallestChunkingTruncateMask = m_dwSmallestChunkingTruncateMask;      // The smallest mask (default = 16-6 = 10 bits ) 
                DWORD dwSmallestChunkingHashMatchValue = m_dwSmallestChunkingHashMatchValue;  // Used for chunking: if the least significant N bytes from the Rabin hash are equal to this value, we declare a "cut" (where N depends on the context)

                DWORD[] arrRegressChunkingTruncateMask = m_arrRegressChunkingTruncateMask;
                DWORD[] arrRegressChunkingHashMatchValue = m_arrRegressChunkingHashMatchValue;

                //
                // Chunking loop
                //

                while (remainingBytes > 0)
                {
                    NT_ASSERT(startOffset >= 0);

                    //
                    // Check to see if the available bytes (remaining + previous) are insufficient to create ore "real" (i.e. non-minimum) chunks
                    //

                    // If the remaining bytes plus the previous "partial chunk" are less than the minimum chunk size, wrap up and exit
                    size_t remainingBytesToBeReported = (size_t)(remainingBytes) + previouslyProcessedBytes;
                    NT_ASSERT(remainingBytesToBeReported > 0);

                    if (remainingBytesToBeReported < m_nMinChunkSize)
                    {
                        // If we had a zero run previously, check to see if all the remaining bytes are all zeros
                        // If yes, m_numZeroRun will be the size of the last "zero chunk" 
                        if (m_numZeroRun >= 0)
                        {
                            NT_ASSERT(m_numZeroRun == (OffsetT)(previouslyProcessedBytes));

                            byte* pStartZeroTest = pStartBuffer + startOffset;
                            DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)pStartZeroTest);

                            // Check how many subsequent consecutive bytes are zeros
                            OffsetT remainingNonZero = remainingBytes;

                            // TODO:365262 - move this in a C++ utility routine
                            // Note: we used DDP_IS_VALID_POINTER to "hide" OACR failures as Prefast can't keep up with the large list of asumptions (known bug)
                            while (DDP_IS_VALID_POINTER((IntPtr)pStartZeroTest) && (remainingNonZero > 0) && ((*pStartZeroTest) == 0))
                            {
                                remainingNonZero--;

                                pStartZeroTest++;
                            }

                            // Check if we found a non-zero byte
                            if (remainingNonZero > 0)
                            {
                                // A non-zero byte was encountered
                                m_numZeroRun = -1;
                            }
                            else
                            {
                                NT_ASSERT(pStartZeroTest <= pEndBuffer);

                                // All remaining bytes were zeros 
                                m_numZeroRun += remainingBytes;
                                NT_ASSERT(m_numZeroRun == (OffsetT)(remainingBytesToBeReported));
                            }
                        }

                        // Check if this is the last chunk in the last call sequence
                        if (bNoMoreData)
                        {
                            // Add the final chunk, as its size is smaller than the minimum chunk size
                            // TODO:365262 - use here DDP_CCT_MinReached always (technically this is a MinReached) plus mix DDP_CCT_MinReached and DDP_CCT_All_Zero as flags 
                            // TODO:365262 - use AddChunk
                            AddChunkInfo(
                                new DedupBasicChunkInfo(
                                    lastChunkAbsoluteOffset,
                                    remainingBytesToBeReported,
                                    (m_numZeroRun >= 0) ? DedupChunkCutType.DDP_CCT_All_Zero : DedupChunkCutType.DDP_CCT_MinReached));

                            //
                            // Reset all state related with the cross-call sequence
                            //

                            // TODO:365262 - move the state reset in a utility
                            // TODO:365262 - reset other members such as m_numZeroRun, m_regressChunk, etc

                            hash = 0;
                            previouslyProcessedBytes = 0;
                            lastChunkAbsoluteOffset += remainingBytesToBeReported;
                            m_numZeroRun = 0;

                            // TODO:365262 - assert that we reached the end of the buffer
                            // TODO:365262 - cleanup alg (visible exit here for code clarity)
                        }
                        else
                        {
                            // This is a "partial" chunk - the remainder will be processed in the next call
                            previouslyProcessedBytes += (size_t)(remainingBytes);
                        }

                        // Add remainingBytes to the processed data
                        startOffset += remainingBytes;
                        remainingBytes = 0;

                        // Nothing left to process in this call. Exit the loop
                        break;
                    }

                    //
                    //  Given the treatment above, available bytes (remaining + previous) is at least MinChunkSize. Chunking can now proceed
                    //

                    NT_ASSERT(remainingBytesToBeReported >= m_nMinChunkSize);

                    // Calculate the amount of bytes that could be skipped (since we can skip in the hash calculation the first m_nMinChunkSize bytes)
                    OffsetT bytesToBeSkipped = (OffsetT)(m_nMinChunkSize) - (OffsetT)(previouslyProcessedBytes);
                    NT_ASSERT(remainingBytes >= bytesToBeSkipped); // should be always true given the "if" test above

                    //
                    // Calculate start window 
                    //

                    OffsetT initialStartWindow = 0;        // Beginning of the byte window for Rabin hash calculation. This offset is relative to the beginning of the buffer

                    // Check if we need to perform a "jump" in the data since, if we just made a chunk cut earlier, we can skip the current offset beyond the m_nMinChunkSize since the last cut
                    // Note - since both values are either zero or positive, this test essentially checks if at least one of the values is non-zero
                    if (startOffset + bytesToBeSkipped > 0)
                    {
                        // End of the byte window for Rabin hash calculation. This offset is relative to the beginning of the buffer
                        OffsetT initialEndWindow = startOffset + bytesToBeSkipped;

                        // Note: this can end up slightly negative (but within the window size). This is OK.
                        initialStartWindow = initialEndWindow - (OffsetT)(m_nWindowSize);

                        // Add zero run detection up to the end of the window, including bytes to be skipped
                        if (m_numZeroRun >= 0)
                        {
                            // Scan till end window, is it all zeros?
                            byte* pStartZeroTest = pStartBuffer + startOffset;
                            DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)pStartZeroTest);

                            byte* pEndPosZeroTest = pStartBuffer + initialEndWindow;
                            DDP_ASSERT_VALID_BUFFER_END((IntPtr)pEndPosZeroTest);

                            // Note: we used DDP_IS_VALID_POINTER to "hide" OACR failures as Prefast can't keep up with the large list of asumptions (known bug)
                            while (DDP_IS_VALID_POINTER((IntPtr)pStartZeroTest) && (pStartZeroTest != pEndPosZeroTest) && ((*pStartZeroTest) == 0))
                            {
                                pStartZeroTest++;
                            }

                            // Note: here m_numZeroRun can go beyond previouslyProcessedBytes
                            if (pStartZeroTest != pEndPosZeroTest)
                            {
                                DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)pStartZeroTest);
                                m_numZeroRun = -1;
                            }
                            else
                            {
                                NT_ASSERT(initialEndWindow >= startOffset);
                                m_numZeroRun += initialEndWindow - (OffsetT)(startOffset);
                            }
                        }

                        // We have to make a jump so the previous hash context is lost. Recalculate the hash starting from the new position
                        hash = 0;

                        // Start the hash calculation from the history if the beginning of the window falls outside the current buffer
                        // This could happen if m_nMinChunkSize is just a few bytes below previouslyProcessedBytes, and the content of these bytes were in the previous buffer
                        OffsetT currentStartIndex = initialStartWindow;
                        for (; currentStartIndex < 0; currentStartIndex++)
                        {
                            HashValueT origHash = hash;
                            hash <<= 8;

                            OffsetT nHistoryIndex = currentStartIndex + (long)m_nWindowSize;
                            DDP_ASSERT_VALID_ARRAY_INDEX(nHistoryIndex, m_history);
                            hash ^= m_history[nHistoryIndex];
                            hash ^= g_arrPolynomialsTD[(origHash >> 56) & 0xff];
                        }

                        // Perform the Rabin hash calculation on the remaining bytes in the window
                        NT_ASSERT(currentStartIndex >= 0);
                        byte* pbMark = pStartBuffer + currentStartIndex;
                        DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)pbMark);

                        // Compute the hash for the remaining bytes (within the window) in the actual buffer
                        for (; DDP_IS_VALID_POINTER((IntPtr)pbMark) && (currentStartIndex < initialEndWindow) && (pbMark < pEndBuffer); currentStartIndex++)
                        {
                            HashValueT origHash = hash;
                            hash <<= 8;

                            hash ^= *pbMark;
                            hash ^= g_arrPolynomialsTD[(origHash >> 56) & 0xff];

                            pbMark++;
                        }

                        // Reset m_regressChunkLen array as we just did a jump
                        // TODO:365262 - move this to a utility routine (note: also used in the constructor)
                        for (size_t regressIndex = 0; regressIndex < m_nRegressSize; regressIndex++)
                        {
                            DDP_ASSERT_VALID_ARRAY_INDEX(regressIndex, m_regressChunkLen);
                            m_regressChunkLen[regressIndex] = -1;
                        }

                        m_lastNonZeroChunkLen = -1;
                    }
                    else
                    {
                        initialStartWindow = -(OffsetT)(m_nWindowSize);
                    }

                    //
                    // Get pointers to the beginning of the window start/end
                    //

                    // Pointer to the start of the window
                    byte* pStartWindow = pStartBuffer + initialStartWindow;
                    DDP_ASSERT_VALID_START_POINTER((IntPtr)pStartWindow);

                    // Pointer to the end of the window 
                    byte* pEndWindow = pStartWindow + m_nWindowSize;
                    DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                    // Pointer to the byte where the maximum chunk size is hit (or to the end of the current buffer, if the max is beyond reach)
                    NT_ASSERT(m_nMaxChunkSize > previouslyProcessedBytes);
                    OffsetT bytesUntilMax = (OffsetT)(m_nMaxChunkSize) - (OffsetT)(previouslyProcessedBytes);
                    byte* pEndPosUntilMax = pStartBuffer + Math.Min((OffsetT)cbLen, startOffset + bytesUntilMax);
                    DDP_ASSERT_VALID_BUFFER_END((IntPtr)pEndPosUntilMax);

                    // Continue the zero run detection until pEndPosUntilMax
                    if (m_numZeroRun >= 0)
                    {
                        // Note: m_numZeroRun can be larger if we performed a jump (due to zero detection during the jump)
                        NT_ASSERT(m_numZeroRun >= (OffsetT)(previouslyProcessedBytes));

                        bool bDeclareChunk = false;

                        // Find the first non-zero
                        // TODO:365262 consider use utility routine
                        // Note: we used DDP_IS_VALID_POINTER to "hide" OACR failures as Prefast can't keep up with the large list of asumptions (known bug)
                        byte* pPreviousEndWindow = pEndWindow;
                        while (DDP_IS_VALID_POINTER((IntPtr)pEndWindow) && (pEndWindow != pEndPosUntilMax) && ((*pEndWindow) == 0))
                        {
                            pEndWindow++;
                        }

                        DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                        // Get the amount of zero bytes just discovered, and update m_numZeroRun
                        NT_ASSERT(pEndWindow >= pPreviousEndWindow);
                        size_t zeroScanned = (size_t)(pEndWindow - pPreviousEndWindow);
                        m_numZeroRun += (OffsetT)zeroScanned;

                        // Update the number of processed bytes
                        // This includes the bytes discovered above, and the zero-run discovered by "jumping"
                        OffsetT zeroBytes = m_numZeroRun - (OffsetT)(previouslyProcessedBytes);

                        // Check if we need to record a new chunk
                        if (pEndWindow == pEndPosUntilMax)
                        {
                            // All zeros in this run

                            // Check if we reached the end of the buffer without reaching the maximum chunk size
                            if (m_numZeroRun < (OffsetT)(m_nMaxChunkSize))
                            {
                                // We reached the end of the buffer
                                NT_ASSERT(pEndPosUntilMax == (pStartBuffer + cbLen));

                                if (bNoMoreData)
                                {
                                    NT_ASSERT(m_numZeroRun > 0);
                                    bDeclareChunk = true;
                                }
                                else
                                {
                                    // We need to exit as we are at the end of the buffer
                                }
                            }
                            else
                            {
                                NT_ASSERT(m_numZeroRun >= (OffsetT)(m_nMaxChunkSize));
                                bDeclareChunk = true;
                            }
                        }
                        else
                        {
                            DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                            NT_ASSERT(m_numZeroRun >= (OffsetT)(m_nMinChunkSize));
                            bDeclareChunk = true;
                        }

                        if (bDeclareChunk)
                        {
                            // TODO:365262 - use AddChunk
                            NT_ASSERT(m_numZeroRun > 0);
                            AddChunkInfo(
                                new DedupBasicChunkInfo(
                                    lastChunkAbsoluteOffset,
                                    (size_t)(m_numZeroRun),
                                    DedupChunkCutType.DDP_CCT_All_Zero));
                            hash = 0;
                            previouslyProcessedBytes = 0;
                            lastChunkAbsoluteOffset += (size_t)(m_numZeroRun);
                            m_numZeroRun = 0;

                            startOffset += zeroBytes;
                            remainingBytes -= zeroBytes;
                            NT_ASSERT(remainingBytes >= 0);

                            continue;
                        }
                        else
                        {
                            // TODO:365262 - exit here from the routine

                            // No chunk cut yet as we reached the end of the buffer while counting zeros
                            // Update state to incorporate the zeros we just found
                            startOffset += zeroBytes;
                            remainingBytes -= zeroBytes;
                            NT_ASSERT(remainingBytes == 0); // We need to exit as we can't declare a chunk yet

                            NT_ASSERT(m_numZeroRun >= 0);
                            previouslyProcessedBytes = (size_t)(m_numZeroRun);
                            continue;
                        }
                    }

                    //
                    // We are done with zero detection.
                    // Perform the actual hasing + chunking. 
                    //

                    bool bLoopForNextHashValue = true;
                    while (bLoopForNextHashValue)
                    {
                        NT_ASSERT(pStartWindow < pEndWindow);

                        // Advance hash calculation when the start is from the previous buffer, and the end is in the current buffer
                        // TODO:365262 potential perf improvement
                        while (DDP_IS_VALID_POINTER((IntPtr)pEndWindow) &&
                            (dwSmallestChunkingHashMatchValue != (hash & dwSmallestChunkingTruncateMask)) &&
                            pEndWindow < pEndPosUntilMax &&
                            initialStartWindow < 0)
                        {
                            // use history
                            // TODO:365262 - add index check for g_arrPolynomialsXX (static assert)
                            DDP_ASSERT_VALID_ARRAY_INDEX(initialStartWindow + (OffsetT)m_nWindowSize, m_history);
                            hash ^= g_arrPolynomialsTU[m_history[initialStartWindow + (OffsetT)m_nWindowSize]];

                            HashValueT origHash = hash;
                            hash <<= 8;
                            hash ^= *pEndWindow;
                            hash ^= g_arrPolynomialsTD[(origHash >> 56) & 0xff];

                            pStartWindow++;
                            pEndWindow++;
                            initialStartWindow++;
                        }

                        DDP_ASSERT_VALID_START_POINTER((IntPtr)pStartWindow);
                        DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                        // Advance calculation while both window ends are in the same buffer
                        // TODO:365262 potential perf improvement
                        while (DDP_IS_VALID_POINTER((IntPtr)pStartWindow) &&
                               DDP_IS_VALID_POINTER((IntPtr)pEndWindow) &&
                              (dwSmallestChunkingHashMatchValue != (hash & dwSmallestChunkingTruncateMask)) &&
                                pEndWindow < pEndPosUntilMax)
                        {
                            // the main critical loop
                            hash ^= g_arrPolynomialsTU[*pStartWindow];
                            HashValueT origHash = hash;
                            hash <<= 8;
                            hash ^= *pEndWindow;
                            hash ^= g_arrPolynomialsTD[(origHash >> 56) & 0xff];

                            pStartWindow++;
                            pEndWindow++;
                            // Note: we do need not to increment initialStartWindow anymore here (as this was related with the initial setup)
                        }

                        DDP_ASSERT_VALID_START_POINTER((IntPtr)pStartWindow);
                        DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                        // Processed bytes starting from the current startOffset. Equal or smaller than the size of the buffer
                        NT_ASSERT(pEndWindow >= pStartBuffer + startOffset);
                        size_t processedBytes = (size_t)(pEndWindow - (pStartBuffer + startOffset));

                        // Length of the "potential chunk" we found
                        size_t chunkLen = processedBytes + previouslyProcessedBytes;
                        NT_ASSERT(chunkLen <= m_nMaxChunkSize);
                        NT_ASSERT(chunkLen >= m_nMinChunkSize);

                        // Check if a hash-driven chunk cut was made (using the smallest hash/mask)
                        if (dwSmallestChunkingHashMatchValue == (hash & dwSmallestChunkingTruncateMask))
                        {
                            // TODO:365262 use a utility routine
                            OffsetT regressHashMismatchIndex = (OffsetT)m_nRegressSize - 1;
                            for (; regressHashMismatchIndex >= 0; regressHashMismatchIndex--)
                            {
                                // Find the last mask close to m_nMaxChunkSize
                                // TODO:365262 array index check
                                // TODO:365262 Refactor to eliminate the confusing "offset by 1" difference between the two arrays
                                m_regressChunkLen[regressHashMismatchIndex] = (OffsetT)chunkLen;
                                if (arrRegressChunkingHashMatchValue[regressHashMismatchIndex] != (hash & arrRegressChunkingTruncateMask[regressHashMismatchIndex]))
                                    break;
                            }

                            // If we had a match all the way it means that we encountered a match with the full-lenght hash value
                            if (regressHashMismatchIndex < 0)
                            {
                                // Find full mask match

                                // TODO:365262 - use AddChunk
                                AddChunkInfo(
                                    new DedupBasicChunkInfo(lastChunkAbsoluteOffset, chunkLen, DedupChunkCutType.DDP_CCT_Normal));
                                hash = 0;
                                previouslyProcessedBytes = 0;
                                m_numZeroRun = 0;
                                lastChunkAbsoluteOffset += chunkLen;

                                bLoopForNextHashValue = false;
                                startOffset += (OffsetT)processedBytes;
                                remainingBytes -= (OffsetT)processedBytes;

                                break;
                            }
                            else
                            {
                                // Not a full mask match, repeat the logic in the main critical loop, 
                                // We will need to move the pointer forward by one byte, otherwise, it will stuck in the loop
                                if (pEndWindow < pEndPosUntilMax)
                                {
                                    if (initialStartWindow < 0)
                                    {
                                        DDP_ASSERT_VALID_ARRAY_INDEX(initialStartWindow + (OffsetT)m_nWindowSize, m_history);

                                        hash ^= g_arrPolynomialsTU[m_history[initialStartWindow + (OffsetT)m_nWindowSize]];
                                    }
                                    else if (DDP_IS_VALID_POINTER((IntPtr)pStartWindow))
                                    {
                                        hash ^= g_arrPolynomialsTU[*pStartWindow];
                                    }
                                    else
                                    {
                                        NT_ASSERT(false);
                                    }

                                    if (DDP_IS_VALID_POINTER((IntPtr)pEndWindow))
                                    {
                                        HashValueT origHash = hash;
                                        hash <<= 8;
                                        hash ^= *pEndWindow;
                                        hash ^= g_arrPolynomialsTD[(origHash >> 56) & 0xff];
                                    }
                                    else
                                    {
                                        NT_ASSERT(false);
                                    }

                                    pStartWindow++;
                                    DDP_ASSERT_VALID_START_POINTER((IntPtr)pStartWindow);

                                    pEndWindow++;
                                    DDP_ASSERT_VALID_END_POINTER((IntPtr)pEndWindow);

                                    initialStartWindow++;
                                    continue; // To loop for next hash value
                                }
                                else
                                {
                                    // We found a mismatch on a larger chunk but we also reach the end of the chunk. We need to continue with regression
                                    NT_ASSERT(pEndWindow == pEndPosUntilMax);
                                }
                            }
                        };

                        // We reach pEndWindow == pEndPosUntilMax condition. 
                        NT_ASSERT(pEndWindow == pEndPosUntilMax);

                        // Perform regression
                        if (m_nMaxChunkSize == chunkLen)
                        {
                            // Run to m_nMaxChunkSize
                            size_t curChunkLen = chunkLen;
                            DedupChunkCutType cutType = DedupChunkCutType.DDP_CCT_Unknown;

                            size_t lowestValidRegressionIndex;
                            for (lowestValidRegressionIndex = 0; lowestValidRegressionIndex < m_nRegressSize; lowestValidRegressionIndex++)
                            {
                                if (m_regressChunkLen[lowestValidRegressionIndex] >= 0)
                                    break;
                            }

                            size_t potentiallyRegressedChunkLen = chunkLen;

                            // If we found a length we can regress to, get the chunk length and type
                            // Note: this should work if it regresses in Previous Bytes
                            if (lowestValidRegressionIndex < m_nRegressSize)
                            {
                                // We find at least one chunk point with a partial mask match
                                // TODO:365262 - add better type inference that works with a variable-length regression array
                                switch (lowestValidRegressionIndex)
                                {
                                    case 0: cutType = DedupChunkCutType.DDP_CCT_Regress_1_bit; break;
                                    case 1: cutType = DedupChunkCutType.DDP_CCT_Regress_2_bit; break;
                                    case 2: cutType = DedupChunkCutType.DDP_CCT_Regress_3_bit; break;
                                    case 3: cutType = DedupChunkCutType.DDP_CCT_Regress_4_bit; break;
                                    default:
                                        NT_ASSERT(false);
                                        break;
                                }

                                potentiallyRegressedChunkLen = (size_t)(m_regressChunkLen[lowestValidRegressionIndex]);
                                m_regressChunkLen[lowestValidRegressionIndex] = -1;

                                // Adjust the length of subsequent regressions
                                size_t subsequentRegressionIndexes = lowestValidRegressionIndex + 1;
                                for (; subsequentRegressionIndexes < m_nRegressSize; subsequentRegressionIndexes++)
                                {
                                    // All regress point match less bit is adjusted here. 
                                    NT_ASSERT(m_regressChunkLen[subsequentRegressionIndexes] >= (OffsetT)(potentiallyRegressedChunkLen));

                                    m_regressChunkLen[subsequentRegressionIndexes] -= (OffsetT)potentiallyRegressedChunkLen;
                                    if (m_regressChunkLen[subsequentRegressionIndexes] < (OffsetT)(m_nMinChunkSize))
                                    {
                                        m_regressChunkLen[subsequentRegressionIndexes] = -1;
                                    }
                                }

                                // TODO:365262 - clean up the algorithm
                                if (m_lastNonZeroChunkLen != -1)
                                {
                                    if (m_lastNonZeroChunkLen >= (OffsetT)(potentiallyRegressedChunkLen))
                                    {
                                        m_lastNonZeroChunkLen -= (OffsetT)potentiallyRegressedChunkLen;

                                        if (m_lastNonZeroChunkLen < (OffsetT)(m_nMinChunkSize))
                                            m_lastNonZeroChunkLen = -1;
                                    }
                                    else
                                    {
                                        m_lastNonZeroChunkLen = -1;
                                    }
                                }
                            }
                            else
                            {
                                cutType = DedupChunkCutType.DDP_CCT_MaxReached;
                            }

                            NT_ASSERT(DedupChunkCutType.DDP_CCT_Unknown != cutType);

                            // TODO:365262 - use AddChunk
                            AddChunkInfo(
                                new DedupBasicChunkInfo(lastChunkAbsoluteOffset, potentiallyRegressedChunkLen, cutType));

                            // Note: processedBytes is still relative to curChunkLen not potentiallyRegressedChunkLen
                            // If this is the last call in teh dcall sequence, one more chunk will be added at the end 
                            previouslyProcessedBytes = (size_t)((OffsetT)(curChunkLen) - (OffsetT)(potentiallyRegressedChunkLen));
                            lastChunkAbsoluteOffset += potentiallyRegressedChunkLen;
                            startOffset += (OffsetT)processedBytes;
                            remainingBytes -= (OffsetT)processedBytes;

                            // Recalculate m_numZeroRun
                            // TODO:365262 cleanup algorithm 
                            if (previouslyProcessedBytes >= m_nWindowSize)
                            {
                                m_numZeroRun = -1; // This can't be part of a zero run, otherwise it will match the full 16 bit mask. 
                            }
                            else
                            {
                                OffsetT nExaminePos = startOffset - 1;
                                size_t prevBytesAdj = previouslyProcessedBytes;

                                for (; prevBytesAdj > 0; prevBytesAdj--, nExaminePos--)
                                {
                                    if (nExaminePos >= 0)
                                    {
                                        DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)(pStartBuffer + nExaminePos));

                                        if (pStartBuffer[nExaminePos] != 0)
                                            break;
                                    }
                                    else
                                    {
                                        DDP_ASSERT_VALID_ARRAY_INDEX(nExaminePos + (OffsetT)m_nWindowSize, m_history);

                                        if (m_history[nExaminePos + (OffsetT)m_nWindowSize] != 0)
                                            break;
                                    }
                                }

                                if (prevBytesAdj > 0)
                                    m_numZeroRun = -1;
                                else
                                    m_numZeroRun = (OffsetT)previouslyProcessedBytes;
                            }

                            if (previouslyProcessedBytes >= m_nMinChunkSize)
                            {
                                // Loop can continue runs. 
                                bLoopForNextHashValue = true;

                                // Update pEndPosUntilMax
                                // TODO:365262 - cleanup algorithm
                                pEndPosUntilMax = pStartBuffer + Math.Min((OffsetT)cbLen, startOffset + (OffsetT)(m_nMaxChunkSize) - (OffsetT)(previouslyProcessedBytes));
                                DDP_ASSERT_VALID_BUFFER_END((IntPtr)pEndPosUntilMax);
                            }
                            else
                            {
                                // Need to exit loop
                                bLoopForNextHashValue = false;
                            }
                        }
                        else if (bNoMoreData)
                        {
                            // We can't apply regression as we are reaching the end
                            NT_ASSERT(chunkLen > 0);

                            // TODO:365262 - use AddChunk
                            // TODO:MSR - we should declare the "end" consistently (right now we use both MinReached and EndReached)
                            AddChunkInfo(
                                new DedupBasicChunkInfo(lastChunkAbsoluteOffset, chunkLen, DedupChunkCutType.DDP_CCT_EndReached));
                            hash = 0;
                            previouslyProcessedBytes = 0;
                            m_numZeroRun = 0;
                            lastChunkAbsoluteOffset += chunkLen;

                            startOffset += (OffsetT)processedBytes;
                            remainingBytes -= (OffsetT)processedBytes;
                            bLoopForNextHashValue = false;

                            // TODO:365262 - add a more visible exit
                        }
                        else // more data but no regression
                        {
                            // Find first non zero position, make sure we don't run before pStartBuffer
                            byte* pReverseScan = pEndWindow;
                            DDP_ASSERT_VALID_END_POINTER((IntPtr)pReverseScan);

                            byte* pReverseScanStop = pEndPosUntilMax - Math.Min((OffsetT)(chunkLen) - (OffsetT)(m_nMinChunkSize) + 1, pEndPosUntilMax - pStartBuffer);
                            DDP_ASSERT_VALID_END_POINTER((IntPtr)pReverseScanStop);

                            do
                            {
                                pReverseScan--;
                                // DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)pReverseScan);
                            }
                            while (DDP_IS_VALID_POINTER((IntPtr)pReverseScan) && ((*pReverseScan) == 0) && (pReverseScan >= pReverseScanStop));
                            // Note: we used DDP_IS_VALID_POINTER to "hide" OACR failures as Prefast can't keep up with the large list of asumptions (known bug)

                            if (pReverseScan >= pReverseScanStop)
                            {
                                // A first non zero position is found, we chunk at the first zero position afterwards. 
                                m_lastNonZeroChunkLen = (OffsetT)(chunkLen) - (pEndPosUntilMax - pReverseScan - 1);
                                NT_ASSERT(m_lastNonZeroChunkLen >= (OffsetT)(m_nMinChunkSize));
                                NT_ASSERT(m_lastNonZeroChunkLen <= (OffsetT)(m_nMaxChunkSize));
                            };

                            previouslyProcessedBytes += processedBytes;
                            startOffset += (OffsetT)processedBytes;
                            remainingBytes -= (OffsetT)processedBytes;
                            bLoopForNextHashValue = false;
                        }
                    }
                }

                NT_ASSERT(remainingBytes >= 0);

                // Add one last chunk, if this is the last call and the last regression run left some "left-over" buffer
                if ((previouslyProcessedBytes > 0) && bNoMoreData)
                {
                    // End reached, no more data available
                    DedupChunkCutType endChunkType = (m_numZeroRun >= 0)
                        ? DedupChunkCutType.DDP_CCT_All_Zero
                        : (previouslyProcessedBytes < m_nMinChunkSize)
                            ? DedupChunkCutType.DDP_CCT_MinReached
                            : DedupChunkCutType.DDP_CCT_EndReached;
                    AddChunkInfo(new DedupBasicChunkInfo(lastChunkAbsoluteOffset, previouslyProcessedBytes, endChunkType));
                    hash = 0;
                    lastChunkAbsoluteOffset += previouslyProcessedBytes;
                    previouslyProcessedBytes = 0;
                    m_numZeroRun = 0;
                }

                //
                // Save internal state for the next CDedupRegressionChunking::FindRabinChunkBoundariesInternal call
                //

                OffsetT bytesIndex = 0;
                for (; bytesIndex < Math.Min(0, (OffsetT)(m_nWindowSize) - (OffsetT)(cbLen)); bytesIndex++)
                {
                    DDP_ASSERT_VALID_ARRAY_INDEX(bytesIndex, m_history);
                    DDP_ASSERT_VALID_ARRAY_INDEX(bytesIndex + (OffsetT)cbLen, m_history);
                    m_history[bytesIndex] = m_history[bytesIndex + (OffsetT)cbLen];
                }

                for (; bytesIndex < (OffsetT)m_nWindowSize; bytesIndex++)
                {
                    DDP_ASSERT_VALID_BUFFER_POINTER((IntPtr)(pStartBuffer + cbLen - m_nWindowSize + bytesIndex));
                    DDP_ASSERT_VALID_ARRAY_INDEX(bytesIndex, m_history);
                    m_history[bytesIndex] = pStartBuffer[(OffsetT)(cbLen) - (OffsetT)(m_nWindowSize) + bytesIndex];
                }

                m_hash = hash;

                // Save the amount of unprocessed bytes
                previouslyProcessedBytesParam = previouslyProcessedBytes;

                // Save the last absolute chunk offset
                NT_ASSERT(lastChunkAbsoluteOffset >= lastChunkAbsoluteOffsetParam);
                lastChunkAbsoluteOffsetParam = lastChunkAbsoluteOffset;
            }
        }
    }
}
