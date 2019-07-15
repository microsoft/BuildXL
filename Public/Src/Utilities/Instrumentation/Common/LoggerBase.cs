// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;
using System.Runtime.CompilerServices;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Base class for all loggers
    /// </summary>
    public abstract class LoggerBase
    {
        /// <summary>
        /// Gets whether message inspection in enabled.
        /// </summary>
        public virtual bool InspectMessageEnabled => false;

        /// <summary>
        /// Hook for inspecting log messages on a logger
        /// </summary>
        protected virtual void InspectMessage(int logEventId, EventLevel level, string message, Location? location = null)
        {
            // Do nothing. Must be overridden to enable this functionality.
        }

        /// <summary>
        /// See <see cref="LoggingContext.EnqueueLogAction"/>.
        /// </summary>
        protected static void EnqueueLogAction(LoggingContext loggingContext, int logEventId, Action logAction, [CallerMemberName] string eventName = null)
        {
            loggingContext.EnqueueLogAction(logEventId, logAction, eventName);
        }
    }
}
