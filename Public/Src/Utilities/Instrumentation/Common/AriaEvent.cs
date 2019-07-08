// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if FEATURE_ARIA_TELEMETRY

using System;
using System.Collections.Generic;
#if !FEATURE_CORECLR
using Microsoft.Applications.Telemetry;
using Microsoft.Applications.Telemetry.Desktop;
#else

#endif

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
    /// <remarks>
    /// Currently Aria telemetry is only enabled for:
    /// - Windows when building against the full framework .NET assemblies +4.6.1 through Microsoft.Applications.Telemetry
    /// - macOS when building against .NET Core through the native Aria C++ SDK
    /// TODO: Extend this class with more cross platform Aria implementations once they get usable and more mature, ultimately
    ///       use the cross-platform .NETStandard2.0 Aria assemblies.
    /// </remarks>
    public sealed class AriaEvent
    {
#if !FEATURE_CORECLR
        private EventProperties m_eventProperties;
#else
        private List<AriaNative.EventProperty> m_eventProperties;
#endif
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

#if !FEATURE_CORECLR
            m_eventProperties = new EventProperties(name);
#else
            m_eventProperties = new List<AriaNative.EventProperty>();
#endif
        }

        /// <summary>
        /// Sets a property on a concrete Aria event
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property</param>
        public void SetProperty(string name, string value)
        {
#if !FEATURE_CORECLR
            m_eventProperties.SetProperty(name, value);
#else
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = value ?? string.Empty,
                PiiOrValue = (long)PiiType.None
            });
#endif
        }

        /// <summary>
        /// Sets a property on a concrete Aria event in combination with a PII (Personally Identifiable Information) type
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property</param>
        /// <param name="type">The PII type property</param>
        public void SetProperty(string name, string value, PiiType type)
        {
#if !FEATURE_CORECLR
            m_eventProperties.SetProperty(name, value, ConvertPiiType(type));
#else
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = value ?? string.Empty,
                PiiOrValue = (long)type
            });
#endif
        }

        /// <summary>
        /// Sets a property on a concrete Aria event
        /// </summary>
        /// <param name="name">The name property</param>
        /// <param name="value">The value property as a long type</param>
        public void SetProperty(string name, long value)
        {
#if !FEATURE_CORECLR
            m_eventProperties.SetProperty(name, value);
#else
            m_eventProperties.Add(new AriaNative.EventProperty()
            {
                Name = name,
                Value = null,
                PiiOrValue = value
            });
#endif
        }

        /// <summary>
        /// Sends the concrete Aria event to the remote telemetry stream
        /// </summary>
        public void Log()
        {
#if !FEATURE_CORECLR
            LogManager.GetLogger().LogEvent(m_eventProperties);
#else
            AriaNative.LogEvent(AriaV2StaticState.s_AriaLogger, m_eventName, m_eventProperties.ToArray());
            m_eventProperties = null;
#endif
        }

#if !FEATURE_CORECLR
        private Microsoft.Applications.Telemetry.PiiType ConvertPiiType(PiiType type)
        {
            switch (type)
            {
                case PiiType.Identity:
                    return Microsoft.Applications.Telemetry.PiiType.Identity;
                default:
                    return Microsoft.Applications.Telemetry.PiiType.None;
            }
        }
#endif
    }
}
#endif //FEATURE_ARIA_TELEMETRY