// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// A class for shared code related to <see cref="KeyValueStoreAccessor"/> that also requires Scheduler dependencies.
    /// This is done to avoid the <see cref="KeyValueStoreAccessor"/> having to take a dependency on the Scheduler.
    /// </summary>
    public class KeyValueStoreUtilities
    {
        /// <summary>
        /// Checks if a failure was likely a rocksdb exception and logs an event to telemetry accordingly.
        /// Assume any exception seen while using <see cref="KeyValueStoreAccessor"/> that isn't wrapped in <see cref="BuildXLException"/> is assumed to be a rocksdb exception
        /// </summary>
        public static void CheckAndLogRocksDbException(Failure f, LoggingContext loggingContext)
        {
            if (f is Failure<Exception> failure && !(failure.Content is BuildXLException))
            {
                Logger.Log.RocksDbException(loggingContext, failure.Content.ToString());
            }
        }
    }
}
