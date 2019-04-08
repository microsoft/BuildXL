// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VSCode.DebugProtocol
{
    /// <summary>
    /// A structured message object. Used to return errors from requests.
    /// </summary>
    public interface IMessage
    {
        /// <summary>
        /// Unique identifier for the message.
        /// </summary>
        int Id { get; }

        /// <summary>
        /// A message string; variables are not supported in this implementatoin (inline the variables yourself).
        /// </summary>
        string Format { get; }

        /// <summary>
        /// If true send to telemetry.
        /// </summary>
        bool SendTelemetry { get; }

        /// <summary>
        /// If true show user.
        /// </summary>
        bool ShowUser { get; }

        /// <summary>
        /// An optional url where additional information about this message can be found.
        /// </summary>
        string Url { get; }

        /// <summary>
        /// An optional label that is presented to the user as the UI for opening the url.
        /// </summary>
        string UrlLabel { get; }
    }
}
