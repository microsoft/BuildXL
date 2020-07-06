// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Provide transaction semantics for a reservation request.
    /// </summary>
    public sealed class ReserveTransaction
    {
        private readonly ReserveSpaceRequest _request;
        private readonly Action<ReserveSpaceRequest> _commitAction;

        /// <nodoc />
        internal ReserveTransaction(ReserveSpaceRequest request, Action<ReserveSpaceRequest> commitAction)
        {
            Contract.Requires(request != null);
            Contract.Requires(commitAction != null);

            _request = request;
            _commitAction = commitAction;
        }

        /// <summary>
        ///     Finalize reservation on successful content addition.
        /// </summary>
        public void Commit()
        {
            _commitAction?.Invoke(_request);
        }
    }
}
