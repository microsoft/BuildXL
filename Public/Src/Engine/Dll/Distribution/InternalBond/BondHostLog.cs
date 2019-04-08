// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System.Globalization;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// ILog to log bond host events to BuildXL
    /// </summary>
    internal sealed class BondHostLog
    {
        private LoggingContext m_loggingContext;
        private string m_ipAddress;
        private int m_port;

        public BondHostLog(LoggingContext loggingContext, string ipAddress, int port)
        {
            m_loggingContext = loggingContext;
            m_ipAddress = ipAddress;
            m_port = port;
        }

        public void Write(Severity severity, string message)
        {
            BuildXL.Engine.Tracing.Logger.Log.DistributionHostLog(m_loggingContext, m_ipAddress, m_port, severity.ToString(), message);
        }

        public void Debug(string format, params object[] args)
        {
            Write(Severity.Debug, string.Format(CultureInfo.InvariantCulture, format, args));
        }

        public enum Severity
        {
            Unknown = 0,
            Diagnostic = 1,
            Debug = 2,
            Info = 3,
            Warning = 4,
            Error = 5,
            Fatal = 6,
            Always = 7,
        }
    }
}
#endif
