// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using Microsoft.Bond;
using Void = Microsoft.Bond.Void;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Represents a bond interface with a heartbeat method for use by BondProxyConnectionManager
    /// </summary>
    internal interface IBondProxyWithHeartbeat
    {
        /// <summary>
        /// Ends a heartbeat request
        /// </summary>
        Message<Void> EndHeartbeat(IAsyncResult result);

        /// <summary>
        /// Cancels a heartbeat request
        /// </summary>
        void CancelHeartbeat(IAsyncResult result);

        /// <summary>
        /// Begins a request with the given function name
        /// </summary>
        /// <typeparam name="T">the input type</typeparam>
        /// <param name="methodName">the service method name</param>
        /// <param name="input">the input value to send to service</param>
        /// <param name="callback">the async callback to signal completion</param>
        /// <param name="allocator">the buffer allocator used to allocate buffers for the call</param>
        /// <returns>the async result</returns>
        IAsyncResult BeginRequest<T>(string methodName, Message<T> input, AsyncCallback callback, IBufferAllocator allocator) where T : IBondSerializable, new();

        /// <summary>
        /// Ends the request and returns the result
        /// </summary>
        Message<T> EndRequest<T>(string methodName, IAsyncResult res) where T : IBondSerializable, new();

        /// <summary>
        /// Cancels the request
        /// </summary>
        void CancelRequest(string methodName, IAsyncResult res);
    }
}
#endif
