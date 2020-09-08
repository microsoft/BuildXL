// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using Grpc.Core.Logging;

namespace BuildXL.Plugin.Logging
{
    internal sealed class TextLogger : PluginLoggerBase
    {
        private bool m_disposed = false;
        private readonly TextWriter m_writer;
        private const string DateTimeFormat = "HH:mm:ss.fff";

        public TextLogger(TextWriter writer) : this(writer, null) { }

        public TextLogger(TextWriter writer, Type type) : base(type)
        {
            m_writer = writer;
        }

        protected override void Log(string level, string message)
        {
            string datetime = DateTime.UtcNow.ToString(DateTimeFormat, CultureInfo.InvariantCulture);
            m_writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {3} [{1}] {2}", datetime, level, message, ForTypeString));
        }

        /// <inheritdoc />
        public override ILogger ForType<T>()
        {
            if (AssignedType == typeof(T))
            {
                return this;
            }

            return new TextLogger(m_writer, typeof(T));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            if (!m_disposed)
            {
                m_disposed = true;
                m_writer?.Dispose();
            }
        }
    }
}
