// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    internal sealed class ForwardedLogger : PluginLoggerBase
    {
        private readonly Action<string, string> m_logAction;

        /// <nodoc />
        public ForwardedLogger(Action<string, string> logAction) : this(logAction, null) { }

        /// <nodoc />
        public ForwardedLogger(Action<string, string> logAction, Type type) : base(type)
        {
            m_logAction = logAction;
        }

        protected override void Log(string level, string message)
        {
            m_logAction(level, message);
        }

        /// <inheritdoc />
        public override ILogger ForType<T>()
        {
            if (AssignedType == typeof(T))
            {
                return this;
            }

            return new ForwardedLogger(m_logAction, typeof(T));
        }

        /// <inheritdoc />
        public override void Dispose() { }
    }
}
