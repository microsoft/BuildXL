// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Event that is code generated
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class GeneratedEventAttribute : Attribute
    {
        /// <summary>
        /// The EventId
        /// </summary>
        public ushort EventId { get; }

        /// <summary>
        /// ETW Keywords for the event
        /// </summary>
        public int Keywords { get; set; }

        /// <summary>
        /// Level for the event
        /// </summary>
        public Level EventLevel { get; set; }

        /// <summary>
        /// ETW Opcode for the event
        /// </summary>
        public byte EventOpcode { get; set; }

        /// <summary>
        /// ETW Task for the event
        /// </summary>
        public ushort EventTask { get; set; }

        /// <summary>
        /// Log generators
        /// </summary>
        public Generators EventGenerators { get; set; }

        /// <summary>
        /// Message format used for the event. Format string consumes the
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Constructor that takes event id.
        /// </summary>
        /// <remarks>
        /// Event Id must be from 0 to 65K otherwise ETW Event Source will throw an ArgumentOutOfRange exception
        /// and will silently disable logger that has at least one Id outside this range.
        /// </remarks>
        public GeneratedEventAttribute(ushort eventId)
        {
            EventId = eventId;
        }
    }

    /// <summary>
    /// Levels
    /// </summary>
    public enum Level
    {
        /// <summary>
        /// Log always
        /// </summary>
        LogAlways = 0,

        /// <summary>
        /// Only critical errors
        /// </summary>
        Critical = 1,

        /// <summary>
        /// All errors, including previous levels
        /// </summary>
        Error = 2,

        /// <summary>
        /// All warnings, including previous levels
        /// </summary>
        Warning = 3,

        /// <summary>
        /// All informational events, including previous levels
        /// </summary>
        Informational = 4,

        /// <summary>
        /// All events, including previous levels
        /// </summary>
        Verbose = 5,
    }

    /// <summary>
    /// Log generators
    /// </summary>
    [Flags]
    public enum Generators
    {
        /// <summary>
        /// No Event Generators
        /// </summary>
        None = 0,

        /// <summary>
        /// Manifested EventSource
        /// </summary>
        ManifestedEventSource = 1,

        /// <summary>
        /// Aria V2
        /// </summary>
        AriaV2 = 2,

        /// <summary>
        /// Statistics
        /// </summary>
        Statistics = 4,

        /// <summary>
        /// Inspectable logger that allows to intersect method calls before writing a log event.
        /// </summary>
        InspectableLogger = 8,

        /// <summary>
        /// Used as a marker that the event would have been generated with AriaV2, but telemetry was disabled. This
        /// is used to differentiate between not having any generators specified, which is a user mistake, and AriaV2
        /// being disabled due to telemetry not being supported in the build configuration.
        /// </summary>
        AriaV2Disabled = 16,
    }

    /// <summary>
    /// Specifies the type that defines EventSource Keywords used.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class EventKeywordsTypeAttribute : Attribute
    {
        /// <summary>
        /// The type that declares the EventKeywords
        /// </summary>
        public Type KeywordsType { get; private set; }

        /// <summary>
        /// Specifies the type that defines EventSource Keywords used.
        /// </summary>
        /// <param name="keywordsType">The type that declares the EventKeywords</param>
        public EventKeywordsTypeAttribute(Type keywordsType)
        {
            KeywordsType = keywordsType;
        }
    }

    /// <summary>
    /// Specifies the type that defines EventSource Tasks
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class EventTasksTypeAttribute : Attribute
    {
        /// <summary>
        /// The type that declares the Event Tasks
        /// </summary>
        public Type TasksType { get; private set; }

        /// <summary>
        /// Specifies the type that defines EventSource Tasks used.
        /// </summary>
        /// <param name="tasksType">The type that declares the Event Tasks</param>
        public EventTasksTypeAttribute(Type tasksType)
        {
            TasksType = tasksType;
        }
    }
}
