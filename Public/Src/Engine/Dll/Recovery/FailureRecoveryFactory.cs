// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Engine.Recovery
{
    /// <summary>
    /// Class for BuildXL failure recovery.
    /// </summary>
    public static class FailureRecoveryFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="FailureRecoveryAggregator"/> specific to BuildXL.
        /// </summary>
        /// <param name="loggingContext">Logging context.</param>
        /// <param name="pathTable">Path table.</param>
        /// <param name="configuration">Configuration.</param>
        /// <returns>An instance of <see cref="FailureRecoveryAggregator"/>.</returns>
        public static FailureRecoveryAggregator Create(LoggingContext loggingContext, PathTable pathTable, IConfiguration configuration)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(pathTable.IsValid);
            Contract.Requires(configuration != null);

            return FailureRecoveryAggregator.Create(
                loggingContext,
                new FailureRecovery[]
                {
                    new CorruptedMemosDbRecovery(pathTable, configuration),
                    new CatastrophicFailureRecovery(pathTable, configuration, loggingContext)
                });
        }
    }
}
