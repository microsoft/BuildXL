// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Provide transaction semantics for a reservation request.
    /// </summary>
    public sealed class ReserveTransaction : IDisposable
    {
        private readonly ReserveSpaceRequest _request;
        private readonly Action<ReserveSpaceRequest> _commitAction;
        private bool _committed;
        private readonly long _size;
        private readonly Action<long> _rollbackAction;

        /// <nodoc />
        internal ReserveTransaction(ReserveSpaceRequest request, Action<ReserveSpaceRequest> commitAction)
        {
            Contract.Requires(request != null);
            Contract.Requires(commitAction != null);

            _request = request;
            _commitAction = commitAction;
        }

        /// <nodoc />
        internal ReserveTransaction(long size, Action<long> rollbackAction)
        {
            Contract.Requires(rollbackAction != null);

            _size = size;
            _rollbackAction = rollbackAction;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (!_committed)
            {
                _rollbackAction?.Invoke(_size);
            }
        }

        /// <summary>
        ///     Finalize reservation on successful content addition.
        /// </summary>
        public void Commit()
        {
            _committed = true;
            _commitAction?.Invoke(_request);
        }
    }
}
