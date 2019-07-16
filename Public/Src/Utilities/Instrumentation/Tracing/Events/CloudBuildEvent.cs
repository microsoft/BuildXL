// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Reflection;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Tracing.CloudBuild
{
    /// <summary>
    /// CloudBuild event kinds
    /// </summary>
    public enum EventKind
    {
        /// <summary>
        /// Reporting the end of BuildXL process
        /// </summary>
        DominoCompleted,

        /// <summary>
        /// Reporting targets added to the graph
        /// </summary>
        TargetAdded,

        /// <summary>
        /// Reporting that the target has started running
        /// </summary>
        TargetRunning,

        /// <summary>
        /// Reporting the failure of the target
        /// </summary>
        TargetFailed,

        /// <summary>
        /// Reporting the completion of the target
        /// </summary>
        TargetFinished,

        /// <summary>
        /// Reporting the beginning of BuildXL process
        /// </summary>
        DominoInvocation,

        /// <summary>
        /// Reporting the outcome of the "drop create" operation
        /// </summary>
        DropCreation,

        /// <summary>
        /// Reporting the outcome of the "drop finalize" operation
        /// </summary>
        DropFinalization,

        /// <summary>
        /// Reporting periodic execution statistics of BuildXL.
        /// </summary>
        DominoContinuousStatistics,

        /// <summary>
        /// Reporting final execution statistics of BuildXL.
        /// </summary>
        DominoFinalStatistics,

        // TODO: When adding a new CloudBuild event, you have to add it to the GetType() method below
    }

    /// <summary>
    /// CloudBuildEvent
    /// </summary>
    public abstract class CloudBuildEvent
    {
        /// <summary>
        /// Event kind
        /// </summary>
        public abstract EventKind Kind { get; set; }

        /// <summary>
        /// Event version
        /// </summary>
        /// <remarks>
        /// Needs to be incremented whenever we update the event.
        /// </remarks>
        public abstract int Version { get; set; }

        /// <summary>
        /// Return the array of current type's properties
        /// </summary>
        /// <remarks>
        /// We do not want to get properties with reflection during parsing each event from TraceEvent because it is expensive.
        /// That's why, we store the properties of each type in a static variable.
        /// </remarks>
        internal abstract PropertyInfo[] Members { get; }

        /// <summary>
        /// Mapping between each derived type name and type object
        /// </summary>
        /// <remarks>
        /// For each event, we cannot afford calling Type.GetType(string) so I keep the mapping in a static variable.
        /// Whenever we add a CloudBuildEvent, we need to add it to this dictionary.
        /// </remarks>
        private static Type GetType(string name)
        {
            switch (name)
            {
                case nameof(TargetAddedEvent):
                    return typeof(TargetAddedEvent);

                case nameof(TargetRunningEvent):
                    return typeof(TargetRunningEvent);

                case nameof(TargetFailedEvent):
                    return typeof(TargetFailedEvent);

                case nameof(TargetFinishedEvent):
                    return typeof(TargetFinishedEvent);

                case nameof(DominoCompletedEvent):
                    return typeof(DominoCompletedEvent);

                case nameof(DominoInvocationEvent):
                    return typeof(DominoInvocationEvent);

                case nameof(DropCreationEvent):
                    return typeof(DropCreationEvent);

                case nameof(DropFinalizationEvent):
                    return typeof(DropFinalizationEvent);

                case nameof(DominoContinuousStatisticsEvent):
                    return typeof(DominoContinuousStatisticsEvent);

                case nameof(DominoFinalStatisticsEvent):
                    return typeof(DominoFinalStatisticsEvent);

                    // Add new types here
            }

            return null;
        }

        /// <summary>
        /// Recreate a <see cref="CloudBuildEvent"/> object whose concrete type name is <paramref name="eventName"/>.
        /// </summary>
        public static Possible<CloudBuildEvent> TryParse(string eventName, IList<object> values)
        {
            Type eventType = GetType(eventName);
            if (eventType == null)
            {
                return new Possible<CloudBuildEvent>(
                    new Failure<string>(I($"'{eventName}' is not found in the derived types of CloudBuildEvent.")));
            }

            if (values.Count != 1)
            {
                return new Possible<CloudBuildEvent>(
                    new Failure<string>(I($"CloudBuildEvents should have only one payload value but '{eventName}' has {values.Count}.")));
            }

            // When you send custom class objects (known as 'rich eventsource payload') through ETW,
            // The listener receives a dictionary whose keys are public member names of this custom class.
            var rawEventObject = values[0] as IDictionary<string, object>;

            if (rawEventObject == null)
            {
                return new Possible<CloudBuildEvent>(
                    new Failure<string>(I($"CloudBuildEvents should send custom class objects in the Dictionary format but '{eventName}' sent {values[0]}.")));
            }

            // This is the fastest way to call the default constructor (faster than activator.createinstance and compiled lambda expression)
            CloudBuildEvent eventObj = eventType.GetConstructor(Type.EmptyTypes).Invoke(null) as CloudBuildEvent;

            // All types in the dictionary, m_typesByName, derive from CloudBuildEvent
            Contract.Assume(eventObj != null);

            try
            {
                foreach (var member in eventObj.Members)
                {
                    object value;
                    if (rawEventObject.TryGetValue(member.Name, out value))
                    {
                        member.SetValue(eventObj, value);
                    }
                }

                return new Possible<CloudBuildEvent>(eventObj);
            }
            catch (Exception e)
            {
                return new Possible<CloudBuildEvent>(new Failure<Exception>(e));
            }
        }
    }
}
