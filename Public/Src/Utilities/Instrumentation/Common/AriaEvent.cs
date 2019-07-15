// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_ARIA_TELEMETRY

using System.Collections.Generic;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// PII types for telemetry, for full list see: https://www.aria.ms/developers/how-to/tag-pii-values/
    /// </summary>
    public enum PiiType
    {
        /// <nodoc />
        None = 0,

        /// <nodoc />
        Identity = 10,
    }

    /// <summary>
    /// Aria Event Wrapper that is aware of target framework and runtime configuration
    /// </summary>
    public sealed class AriaEvent
    {
        private readonly List<AriaNative.EventProperty> m_eventProperties;
        private readonly string m_eventName;
        private readonly string m_targetFramework;
        private readonly string m_targetRuntime;

        /// <summary>
        /// Initializes an underlaying, platform specific Aria event
        /// </summary>
        /// <param name="name">The event name</param>
        /// <param name="targetFramework">The target framework to create the Aria logging facilities for</param>
        /// <param name="targetRuntime">TThe target runtime to create the Aria logging facilities for</param>
        public AriaEvent(string name, string targetFramework, string targetRuntime)
        {
            m_eventName = name;
            m_targetFramework = targetFramework;
            m_targetRuntime = targetRuntime;
            m_eventProperties = new List<AriaNative.EventProperty>();
        }

        /// <summary>
        /// Sets a property on a concrete Aria event
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property</param>
        public void SetProperty(string name, string value)
        {
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = value ?? string.Empty,
                PiiOrValue = (long)PiiType.None
            });
        }

        /// <summary>
        /// Sets a property on a concrete Aria event in combination with a PII (Personally Identifiable Information) type
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property</param>
        /// <param name="type">The PII type property</param>
        public void SetProperty(string name, string value, PiiType type)
        {
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = value ?? string.Empty,
                PiiOrValue = (long)type
            });
        }

        /// <summary>
        /// Sets a property on a concrete Aria event
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property as a long type</param>
        public void SetProperty(string name, long value)
        {
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = null,
                PiiOrValue = value
            });
        }

        /// <summary>
        /// Sends the concrete Aria event to the remote telemetry stream
        /// </summary>
        public void Log()
        {
            AriaV2StaticState.LogEvent(m_eventName, m_eventProperties.ToArray());
            m_eventProperties.Clear();
        }
    }
}
#endif //FEATURE_ARIA_TELEMETRY