// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

#if MICROSOFT_INTERNAL && NET_COREAPP_60
using Microsoft.Applications.Events;
#endif

namespace BuildXL.Utilities.Instrumentation.Common
{
    internal interface IAriaLogger : IDisposable
    {
        public void LogEvent(string eventName, AriaNative.EventProperty[] eventProperties);
    }

    internal static class AriaLoggerFactory
    {
        public static IAriaLogger? CreateAriaLogger(string token, string db, int teardownTimeoutInSeconds)
        {
            return Environment.OSVersion.Platform == PlatformID.Unix
#if MICROSOFT_INTERNAL && NET_COREAPP_60
                ? new XPlatAriaLogger(token)
#else
                ? null
#endif
                : new NativeAriaLogger(AriaNative.CreateAriaLogger(token, db, teardownTimeoutInSeconds));
        }
    }

    internal sealed class NativeAriaLogger : IAriaLogger
    {
        private readonly IntPtr m_logger;
        public NativeAriaLogger(IntPtr logger)
        {
            m_logger = logger;
        }

        public void LogEvent(string eventName, AriaNative.EventProperty[] eventProperties)
            => AriaNative.LogEvent(m_logger, eventName, eventProperties);

        public void Dispose()
            => AriaNative.DisposeAriaLogger(m_logger);
    }

#if MICROSOFT_INTERNAL && NET_COREAPP_60
    internal sealed class XPlatAriaLogger : IAriaLogger
    {
        private readonly Microsoft.Applications.Events.ILogger m_ariaLogger;
        private bool m_disposed;

        public XPlatAriaLogger(string token)
        {
            LogManager.Start(new LogConfiguration());
            m_ariaLogger = LogManager.GetLogger(token, out EVTStatus status);
            CheckStatus("GetLogger", status);
        }

        public void LogEvent(string eventName, AriaNative.EventProperty[] eventProperties)
        {
            var props = new EventProperties { Name = eventName };
            foreach (var prop in eventProperties)
            {
                var status = prop.Value == null || prop.Value.Length == 0
                    ? props.SetProperty(prop.Name, prop.PiiOrValue, PiiKind.None)
                    : props.SetProperty(prop.Name, prop.Value, (PiiKind)prop.PiiOrValue);
                CheckStatus("SetProperty", status);
            }

            CheckStatus("LogEvent", m_ariaLogger.LogEvent(props));
        }

        public void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                LogManager.UploadNow();
                LogManager.Teardown();
            }
        }

        private void CheckStatus(string operationName, EVTStatus status)
        {
            if (status != EVTStatus.OK)
            {
                // anything meaningful we can do here?
            }
        }
    }
#endif
}