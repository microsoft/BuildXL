// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// Event message for "output" event type.
    /// The event indicates that the target has produced output.
    /// </summary>
    public interface IOutputEvent : IEvent<IOutputEventBody> { }

    /// <summary>
    /// Body for <code cref="IOutputEvent"/>
    /// </summary>
    public interface IOutputEventBody
    {
        /// <summary>
        /// The category of output (such as: 'console', 'stdout', 'stderr', 'telemetry'). If not specified, 'console' is assumed.
        /// </summary>
        string Category { get; }

        /// <summary>
        /// The output to report.
        /// </summary>
        string Output { get; }

        /// <summary>
        /// Optional data to report. For the 'telemetry' category the data will be sent to telemetry,
        /// for the other categories the data is shown in JSON format.
        /// </summary>
        object Data { get; }
    }
}
