using System;

namespace BuildXL.Utilities.SBOMUtilities
{
    /// <summary>
    /// Logging wrapper for the SBOM converter.
    /// </summary>
    public class SBOMConverterLogger
    {
        private readonly Action<string> m_infoLogger;
        private readonly Action<string> m_warningLogger;
        private readonly Action<string> m_errorLogger;

        /// <nodoc/>
        public SBOMConverterLogger(Action<string> infoLogger, Action<string> warningLogger, Action<string> errorLogger)
        {
            m_infoLogger = infoLogger;
            m_warningLogger = warningLogger;
            m_errorLogger = errorLogger;
        }

        /// <summary>
        /// Log informational message.
        /// </summary>
        public void Info(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                m_infoLogger(message);
            }
        }

        /// <summary>
        /// Log warning.
        /// </summary>
        public void Warning(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                m_warningLogger(message);
            }
        }

        /// <summary>
        /// Log error.
        /// </summary>
        public void Error(string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                m_errorLogger(message);
            }
        }
    }
}
