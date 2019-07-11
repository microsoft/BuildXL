// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Pips;
using BuildXL.Pips.Operations;

namespace BuildXL.Scheduler
{
    public static partial class PipExecutor
    {
        /// <summary>
        /// The result of storing the two phase cache entry
        /// </summary>
        internal sealed class StoreCacheEntryResult
        {
            /// <summary>
            /// Gets result indicate the the operation successfully stored the cache entry to the cache
            /// </summary>
            public static readonly StoreCacheEntryResult Succeeded = new StoreCacheEntryResult();

            /// <summary>
            /// The execution result if the cache entry had conflicting entry which was retrieved
            /// and outputs were reported from the entry
            /// </summary>
            public readonly ExecutionResult ConvergedExecutionResult;

            /// <summary>
            /// Gets whether the cache entry had conflicting entry which was retrieved
            /// and outputs were reported from the entry
            /// </summary>
            public bool Converged => ConvergedExecutionResult != null;

            /// <summary>
            /// Creates a <see cref="StoreCacheEntryResult"/> indicating cache convergence.
            /// </summary>
            public static StoreCacheEntryResult CreateConvergedResult(ExecutionResult convergedExecutionResult)
            {
                Contract.Requires(convergedExecutionResult != null);
                Contract.Requires(!convergedExecutionResult.IsSealed);

                convergedExecutionResult.Converged = true;
                convergedExecutionResult.Seal();

                Contract.Assert(!convergedExecutionResult.Result.IndicatesFailure(), "Converged result must represent success");
                return new StoreCacheEntryResult(convergedExecutionResult);
            }

            private StoreCacheEntryResult(ExecutionResult convergedExecutionResult = null)
            {
                ConvergedExecutionResult = convergedExecutionResult;
            }
        }
    }
}
