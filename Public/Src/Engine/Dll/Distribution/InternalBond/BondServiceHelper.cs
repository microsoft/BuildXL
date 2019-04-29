// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BondTransport;
using Microsoft.Bond;
using Void = Microsoft.Bond.Void;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Helper class for implementing a Bond service
    /// </summary>
    /// <remarks>
    /// These utilities were imported from BuildCache to handle dependency issues
    /// </remarks>
    public static class BondServiceHelper
    {
        /// <summary>
        /// Handles an incoming Bond void() request
        /// </summary>
        /// <typeparam name="TRequest">Type of the request message</typeparam>
        /// <param name="call">The Bond call request</param>
        /// <param name="handler">Request handler</param>
        public static void HandleRequest<TRequest>(
            Request<TRequest, Void> call,
            Action<TRequest> handler)
            where TRequest : IBondSerializable, new()
        {
            Contract.Requires(call != null);
            Contract.Requires(handler != null);

            HandleRequest(call, request =>
            {
                handler(request);
                return new Void();
            });
        }

        /// <summary>
        /// Handles an incoming Bond request with a response, forwarding exceptions to the caller
        /// </summary>
        /// <typeparam name="TRequest">Type of the request message</typeparam>
        /// <typeparam name="TResponse">Type of the response message</typeparam>
        /// <param name="call">The Bond call request</param>
        /// <param name="handler">Request handler</param>
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        public static void HandleRequest<TRequest, TResponse>(
            Request<TRequest, TResponse> call,
            Func<TRequest, TResponse> handler)
            where TRequest : IBondSerializable, new()
            where TResponse : IBondSerializable, new()
        {
            Contract.Requires(call != null);
            Contract.Requires(handler != null);
            try
            {
                TResponse response = handler(call.RequestObject);
                call.Dispatch(response);
            }
            catch (Exception handlerException)
            {
                call.DispatchException(handlerException);
            }
        }
    }
}
#endif
