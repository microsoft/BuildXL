// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using Microsoft.Bond;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Represents provider for bond output buffers for serialization
    /// </summary>
    public interface IBufferProvider : IDisposable
    {
        /// <summary>
        /// Gets the buffer allocator
        /// </summary>
        IBufferAllocator Allocator { get; }

        /// <summary>
        /// Gets the output buffer
        /// </summary>
        Bond.IO.Safe.OutputBuffer GetOutputBuffer();

        /// <summary>
        /// Indicates that the output buffer is no longer in used.
        /// </summary>
        /// <remarks>
        /// The buffer's current position should be set to the end of the current used space in the buffer
        /// to reserve that segment of the buffer.
        /// </remarks>
        void ReleaseOutputBuffer();
    }
}
#endif