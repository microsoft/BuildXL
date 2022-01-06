// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.SandboxedProcessExecutor
{
    /// <summary>
    /// Simple console logger.
    /// </summary>
    internal class ConsoleLogger
    {
        private readonly object m_lock = new object();

        /// <summary>
        /// Logs informational message.
        /// </summary>
        public void LogInfo(string message)
        {
            LogMessage(message, MessageType.Info);
        }

        /// <summary>
        /// Logs error message.
        /// </summary>
        public void LogError(string message)
        {
            LogMessage(message, MessageType.Error);
        }

        /// <summary>
        /// Logs warning message.
        /// </summary>
        public void LogWarn(string message)
        {
            LogMessage(message, MessageType.Warn);
        }

        private void LogMessage(string message, MessageType type)
        {
            lock (m_lock)
            {
                ConsoleColor original = Console.ForegroundColor;
                string dateTime = DateTime.UtcNow.ToString("yyyy.MM.dd HH:mm:ss.fff");
                message = $"{dateTime} [{type.ToString().ToUpperInvariant()}]: {message}";
                
                switch (type)
                {
                    case MessageType.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.Error.WriteLine(message);
                        break;
                    case MessageType.Warn:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.Out.WriteLine(message);
                        break;
                    case MessageType.Info:
                        Console.Out.WriteLine(message);
                        break;
                }

                Console.ForegroundColor = original;
            }
        }

        private enum MessageType : byte
        {
            Info,
            Warn,
            Error
        }
    }
}
