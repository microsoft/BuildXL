// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using System.Diagnostics.Tracing;
#endif
using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#endif

namespace Test.BuildXL.Utilities
{
    public sealed class EventTests
    {
        private sealed class TestListener : EventListener
        {
            public EventWrittenEventArgs LastEvent;

            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                LastEvent = eventData;
            }

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                // only track BuildXL event sources...
                if (eventSource is Events)
                {
                    EnableEvents(eventSource, EventLevel.Verbose, (EventKeywords)(-1));
                }
            }
        }

        /// <summary>
        /// This test tries to log all events on <see cref="Events" />.
        /// Note that helpers not tagged with [Event] are not included (consider writing custom tests for those).
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
        [Fact]
        public void AllBasicEvents()
        {
            using (var l = new TestListener())
            {
                MemberInfo[] allEventsMembers = typeof(Events).GetMembers(BindingFlags.Public | BindingFlags.Instance);

                foreach (MemberInfo eventsMember in allEventsMembers)
                {
                    if (eventsMember is MethodInfo)
                    {
                        // Not all members are methods.
                        continue;
                    }

                    var eventAttribute = eventsMember.GetCustomAttribute(typeof(EventAttribute)) as EventAttribute;
                    if (eventAttribute == null)
                    {
                        // Not all members are tagged with [Event] data.
                        continue;
                    }

                    var eventMethod = (MethodInfo)eventsMember;
                    bool containsRelatedActivityId;
                    object[] payload = CreateEventPayload(eventMethod, out containsRelatedActivityId);

                    eventMethod.Invoke(Events.Log, payload);

                    XAssert.AreNotEqual(l.LastEvent.EventId, EventId.None, "Last EventId should not be None. Check payload and Message format.");
                    XAssert.AreEqual(eventAttribute.EventId, l.LastEvent.EventId, "Wrong event ID logged for event {0}", eventMethod.Name);
                    XAssert.AreNotEqual(0, eventAttribute.Task, "Invalid task id == 0 for event {0} (this results in auto-assignment of task id)", eventMethod.Name);
                    XAssert.AreEqual(eventAttribute.Task, l.LastEvent.Task, "Wrong task logged for event {0}", eventMethod.Name);
                    XAssert.AreEqual(eventAttribute.Opcode, l.LastEvent.Opcode, "Wrong opcode logged for event {0}", eventMethod.Name);
                    XAssert.AreEqual(eventAttribute.Level, l.LastEvent.Level, "Wrong level logged for event {0}", eventMethod.Name);
                    XAssert.IsTrue(!containsRelatedActivityId || eventAttribute.Opcode == EventOpcode.Start, "Found related activity ID for non-start event {0}", eventMethod.Name);

                    // EventSource does something weird with the top bits, but the bottom bits should be non-zero.
                    unchecked
                    {
                        XAssert.AreEqual<uint>(
                            (uint)eventAttribute.Keywords,
                            (uint)l.LastEvent.Keywords,
                            "Wrong keywords logged for event {0}",
                            eventMethod.Name);
                        XAssert.AreNotEqual<uint>(0, (uint)eventAttribute.Keywords, "Event {0} should have keywords defined", eventMethod.Name);
                    }

                    XAssert.IsTrue(
                        (eventAttribute.Keywords & Keywords.Diagnostics) == 0 || (eventAttribute.Keywords & Keywords.UserMessage) == 0,
                        "Event {0} specifies both the UserMessage and Diagnostics keywords; these are mutually exclusive.", eventMethod.Name);

                    string formattedMessage;
                    try
                    {
                        formattedMessage = string.Format(CultureInfo.InvariantCulture, l.LastEvent.Message, l.LastEvent.Payload.ToArray());
                    }
                    catch (FormatException ex)
                    {
                        XAssert.Fail(
                            "Formatting the message for event {0}. Maybe the format string does not match the WriteEvent arguments. Exception: {1}",
                            eventMethod.Name,
                            ex.ToString());
                        return;
                    }

                    if (containsRelatedActivityId)
                    {
                        object[] payloadCopy = new object[payload.Length - 1];
                        Array.Copy(payload, 1, payloadCopy, 0, payloadCopy.Length);
                        payload = payloadCopy;
                    }

                    XAssert.AreEqual(
                        payload.Length,
                        l.LastEvent.Payload.Count,
                        "Wrong payload size for event {0} (formatted message: `{1}`)",
                        eventMethod.Name,
                        formattedMessage);

                    for (int i = 0; i < payload.Length; i++)
                    {
                        XAssert.AreEqual(
                            payload[i],
                            l.LastEvent.Payload[i],
                            "Incorrect payload value for event {0}, position {1} (formatted message: `{2}`)",
                            eventMethod.Name,
                            i,
                            formattedMessage);
                    }

                    // We now verify that all strings in the payload appear in the message.
                    // We exclude pip-provenance and phase events for now because for some reason they define GUIDs that never actually
                    // get logged (which is probably not a valid thing to do on an EventSource).
                    if (!l.LastEvent.Message.StartsWith(EventConstants.ProvenancePrefix, StringComparison.Ordinal)
                        && !l.LastEvent.Message.StartsWith(EventConstants.PhasePrefix, StringComparison.Ordinal)
                        && !l.LastEvent.Keywords.HasFlag(Keywords.Performance))
                    {
                        for (int i = 0; i < payload.Length; i++)
                        {
                            var currentPayload = payload[i] as string;

                            if (currentPayload != null)
                            {
                                XAssert.IsTrue(
                                    formattedMessage.Contains(currentPayload),
                                    "The formatted message for event {0} did not include the payload string at index {1}",
                                    eventMethod.Name,
                                    i);
                            }
                        }
                    }

                    if (eventAttribute.EventId != (int)EventId.ErrorEvent)
                    {
                        XAssert.IsTrue(
                            eventAttribute.Level != EventLevel.Error ||
                            (eventAttribute.Keywords & Keywords.UserMessage) == Keywords.UserMessage,
                            "Event {0} marked as Error must be Keywords.UserMessage",
                            eventMethod.Name);
                    }
                }
            }
        }

        private static object[] CreateEventPayload(MethodInfo eventsMethod, out bool containsRelatedActivityId)
        {
            Contract.Requires(eventsMethod != null);

            ParameterInfo[] parameters = eventsMethod.GetParameters();
            containsRelatedActivityId = parameters.Length > 0 && parameters[0].Name == "relatedActivityId";
            return parameters.Select((p, idx) => GetPayloadValueForType(p.ParameterType, idx)).ToArray();
        }

        private static object GetPayloadValueForType(Type payloadEntryType, int index)
        {
            Contract.Requires(payloadEntryType != null);

            if (payloadEntryType == typeof(string))
            {
                return "[String " + index + "]";
            }
            else if (payloadEntryType == typeof(bool))
            {
                return true;
            }
            else if (payloadEntryType == typeof(char))
            {
                return 'X';
            }
            else if (payloadEntryType == typeof(byte) ||
                     payloadEntryType == typeof(double) ||
                     payloadEntryType == typeof(short) ||
                     payloadEntryType == typeof(int) ||
                     payloadEntryType == typeof(long) ||
                     payloadEntryType == typeof(ushort) ||
                     payloadEntryType == typeof(uint) ||
                     payloadEntryType == typeof(ulong) ||
                     payloadEntryType == typeof(float))
            {
                return Convert.ChangeType(100 + index, payloadEntryType, CultureInfo.InvariantCulture);
            }
            else if (payloadEntryType == typeof(Guid))
            {
                return new Guid(100 + index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            }
            else
            {
                Contract.Assert(false, "Unsupported type: " + payloadEntryType.Name);
                return null;
            }
        }

        [Fact]
        public void ReflectionException()
        {
            var types = new Type[1];
            types[0] = typeof(object);

            var exceptions = new Exception[1];
            exceptions[0] = new ArgumentException();

            var ex = new ReflectionTypeLoadException(types, exceptions);

            string message = Events.GetReflectionExceptionMessage(ex);
            XAssert.IsTrue(message.Contains("LoaderExceptions:"));
        }
    }
}
