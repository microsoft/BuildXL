// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// Executor that receives a lambda function to which it delegates all <see cref="ExecuteAsync(IIpcOperation)"/> calls.
    /// </summary>
    public sealed class LambdaIpcOperationExecutor : IIpcOperationExecutor
    {
        private readonly Func<IIpcOperation, IIpcResult> m_executor;

        /// <nodoc />
        public LambdaIpcOperationExecutor(Func<IIpcOperation, IIpcResult> executor)
        {
            Contract.Requires(executor != null);

            m_executor = executor;
        }

        /// <inheritdoc />
        public Task<IIpcResult> ExecuteAsync(IIpcOperation op)
        {
            Contract.Requires(op != null);

            return Task.FromResult(m_executor(op));
        }
    }
}
