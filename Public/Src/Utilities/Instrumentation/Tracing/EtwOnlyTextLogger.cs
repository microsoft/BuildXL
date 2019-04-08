// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Tracing
{
    /// <summary>
    /// A <see cref="IEtwOnlyTextLogger"/> based on generated logger used for sending log messages to ETW.
    /// </summary>
    public sealed class EtwOnlyTextLogger : IEtwOnlyTextLogger
    {
        private const int DesiredMaxMessageSize = 4096;
        private readonly LoggingContext m_loggingContext;
        private readonly string m_logKind;
        private int m_sequenceNumber = 0;

        // HACK: This is used to allow the Cache.Core cache factories to create logs to ETW logger without needing to pipe through a logging context.
        private static LoggingContext s_globalLoggingContext;

        /// <nodoc />
        public EtwOnlyTextLogger(LoggingContext loggingContext, string logKind)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(logKind));

            m_loggingContext = loggingContext;
            m_logKind = logKind;
        }

        /// <inheritdoc />
        public void TextLogEtwOnly(int eventNumber, string eventLabel, string message)
        {
            if (message.Length < DesiredMaxMessageSize)
            {
                TextLogEtwOnlyCore(eventNumber, eventLabel, message, sequenceNumber: Interlocked.Increment(ref m_sequenceNumber));
            }
            else
            {
                // Message is too large. Split into segments on new line boundaries

                // All split messages are given the same sequence number
                var sequenceNumber = Interlocked.Increment(ref m_sequenceNumber);
                using (var pooledStringBuilder = Pools.StringBuilderPool.GetInstance())
                {
                    var stringBuilder = pooledStringBuilder.Instance;
                    foreach (var messageSegment in SplitMessage(message))
                    {
                        messageSegment.CopyTo(stringBuilder);
                        if (stringBuilder.Length > DesiredMaxMessageSize)
                        {
                            TextLogEtwOnlyCore(eventNumber, eventLabel, stringBuilder.ToString(), sequenceNumber);
                            stringBuilder.Clear();
                        }
                    }

                    if (stringBuilder.Length > 0)
                    {
                        TextLogEtwOnlyCore(eventNumber, eventLabel, stringBuilder.ToString(), sequenceNumber);
                    }
                }
            }
        }

        private void TextLogEtwOnlyCore(int eventNumber, string eventLabel, string message, int sequenceNumber)
        {
            Logger.Log.TextLogEtwOnly(
                m_loggingContext,
                sessionId: m_loggingContext.Session.Id,
                logKind: m_logKind,
                sequenceNumber: sequenceNumber,
                eventNumber: eventNumber,
                eventLabel: eventLabel,
                message: message);
        }

        private static IEnumerable<StringSegment> SplitMessage(string message)
        {
            int index = 0;
            while (index < message.Length)
            {
                var newIndex = message.IndexOf('\n', index);
                if (newIndex < 0)
                {
                    newIndex = message.Length;
                }
                else
                {
                    newIndex++;
                }

                yield return new StringSegment(message, index, newIndex - index);
                index = newIndex;
            }
        }

        /// <summary>
        /// Attempts to get global default logging context
        /// </summary>
        public static bool TryGetDefaultGlobalLoggingContext(out LoggingContext loggingContext)
        {
            loggingContext = s_globalLoggingContext;
            return loggingContext != null;
        }

        /// <summary>
        /// Enables ETW logging with the given logging context. NOTE: This is a global process wide state.
        /// </summary>
        public static void EnableGlobalEtwLogging(LoggingContext globalLoggingContext)
        {
            s_globalLoggingContext = globalLoggingContext;
        }
    }
}
