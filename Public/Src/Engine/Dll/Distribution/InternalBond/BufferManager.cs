// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System.Diagnostics.CodeAnalysis;
using Bond.IO.Safe;
using Microsoft.Bond;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Manages pools of byte buffers for bond serialization (new and old)
    /// NOTE: Currently, just uses defaults with the exception of providing smaller OutputBuffers when
    /// translating between new and old bond types
    /// </summary>
    internal sealed class BufferManager
    {
        public BufferManager()
        {
        }

        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public IBufferProvider GetBufferProvider()
        {
            // TODO: Using default buffer provider to see if bond issues are caused by
            // issues with buffer provider implementation
            // var providerWrapper = m_providerPool.GetInstance();
            // providerWrapper.Instance.SetWrapper(providerWrapper);
            // return providerWrapper.Instance;
            return DefaultBufferProvider.Instance;
        }

        /// <summary>
        /// Implementation of IBufferProvider(new bond)/IBufferAllocator(old bond) which
        /// uses creates new buffers for each request
        /// </summary>
        private sealed class DefaultBufferProvider : IBufferProvider
        {
            public static readonly DefaultBufferProvider Instance = new DefaultBufferProvider();

            public IBufferAllocator Allocator
            {
                get
                {
                    // Return default buffer allocator which creates a new buffer
                    return DefaultBufferAllocator.Instance;
                }
            }

            public void Dispose()
            {
            }

            public OutputBuffer GetOutputBuffer()
            {
                // Just allocate a new buffer (256 bytes in size)
                return new OutputBuffer(256);
            }

            public void ReleaseOutputBuffer()
            {
                // Do nothing since this implementation always creates a new buffer
            }
        }
    }
}
#endif
