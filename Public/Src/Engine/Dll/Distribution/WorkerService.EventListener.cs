// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
# endif

namespace BuildXL.Engine.Distribution
{
    public sealed partial class WorkerService
    {
        /// <summary>
        /// Event listener which forwards errors to the master instance
        /// </summary>
        private sealed class ForwardingEventListener : FormattingEventListener
        {
            private readonly WorkerService m_workerService;
            private int m_nextEventId;

            public ForwardingEventListener(WorkerService workerService)
                : base(Events.Log, GetStartTime(), eventMask: GetEventMask())
            {
                m_workerService = workerService;
            }

            private static EventMask GetEventMask()
            {
                // Mask out events about failed call since the call might be
                // from trying to send the notification
                return new EventMask(
                    enabledEvents: null,
                    disabledEvents: DistributionHelpers.DistributionAllMessages.ToArray());
            }

            private static DateTime GetStartTime()
            {
                return Process.GetCurrentProcess().StartTime.ToUniversalTime();
            }

            [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
            protected override async void Output(EventLevel level, int id, string eventName, EventKeywords eventKeywords, string text, bool doNotTranslatePaths = false)
            {
                if ((level != EventLevel.Error) && (level != EventLevel.Warning))
                {
                    return;
                }

                if (((long)eventKeywords & (long)Keywords.NotForwardedToMaster) > 0)
                { 
                    return;
                }

                try
                {
                    await m_workerService.SendEventMessagesAsync(
                        forwardedEvents: new List<EventMessage>(1)
                                         {
                                         new EventMessage()
                                         {
                                             Id = Interlocked.Increment(ref m_nextEventId),
                                             Level = (int)level,
                                             EventKeywords = (long)eventKeywords,
                                             EventId = id,
                                             EventName = eventName,
                                             Text = text,
                                         },
                                         });
                }
                catch (Exception ex) when (ExceptionUtilities.HandleUnexpectedException(ex))
                {
                    // Do nothing. Exception filter handles the logic.
                }
            }
        }
    }
}
