// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Threading;

namespace BuildToolsInstaller.Tests
{
    internal class TestLogger : ILogger
    {
        public int ErrorCount = 0;
        public int WarningCount = 0;
        private readonly StringBuilder m_log = new();
        private readonly StringBuilder m_errors = new(); 
        private readonly StringBuilder m_warnings = new();

        public string FullLog => m_log.ToString();  
        public string Errors => m_errors.ToString();
        public string Warnings => m_warnings.ToString();

        public void Debug(string debugMessage)
        {
            lock (m_log)
            {
                m_log.AppendLine(debugMessage);
            }
        }

        public void Debug(string format, params object[] args)
        {
            Debug(string.Format(format, args));
        }

        public void Error(string errorMessage)
        {
            Interlocked.Increment(ref ErrorCount);
            lock (m_log)
            {
                m_log.AppendLine(errorMessage);
            }

            lock (m_errors)
            {
                m_errors.AppendLine(errorMessage);
            }
        }

        public void Error(string format, params object[] args)
        {
            Error(string.Format(format, args));
        }

        public void Error(Exception ex, string errorMessage)
        {
            Error(errorMessage + Environment.NewLine + ex.Message);
        }

        public void Info(string infoMessage)
        {
            lock (m_log)
            {
                m_log.AppendLine(infoMessage);
            }
        }

        public void Warning(string warningMessage)
        {
            Interlocked.Increment(ref WarningCount);
            lock (m_log)
            {
                m_log.AppendLine(warningMessage);
            }

            lock (m_warnings)
            {
                m_warnings.AppendLine(warningMessage);
            }
        }

        public void Warning(string format, params object[] args)
        {
            Warning(string.Format(format, args));
        }

        public void Warning(Exception ex, string warningMessage)
        {
            Warning(warningMessage + Environment.NewLine + ex.Message);
        }
    }
}
