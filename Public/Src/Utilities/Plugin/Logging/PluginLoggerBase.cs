// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using Grpc.Core.Logging;

namespace BuildXL.Plugin
{
    /// <nodoc />
    public abstract class PluginLoggerBase : ILogger, IDisposable
    {
        /// <nodoc />
        protected readonly Type AssignedType;

        /// <nodoc />
        protected readonly string ForTypeString;

        /// <nodoc />
        public PluginLoggerBase(Type type)
        {
            AssignedType = type;

            if (type != null)
            {
                ForTypeString = type.Name;
            }
            else
            {
                ForTypeString = "";
            }
        }

        /// <nodoc />
        protected abstract void Log(string level, string message);

        /// <inheritdoc />
        public abstract ILogger ForType<T>();

        /// <inheritdoc />
        public void Debug(string message)
        {
            Log("Debug", message);
        }

        /// <inheritdoc />
        public void Debug(string format, params object[] formatArgs)
        {
            Debug(string.Format(CultureInfo.InvariantCulture, format, formatArgs));
        }

        /// <inheritdoc />
        public void Error(string message)
        {
            Log("Error", message);
        }

        /// <inheritdoc />
        public void Error(string format, params object[] formatArgs)
        {
            Error(string.Format(CultureInfo.InvariantCulture, format, formatArgs));
        }

        /// <inheritdoc />
        public void Error(Exception exception, string message)
        {
            Error(message + " " + exception);
        }

        /// <inheritdoc />
        public void Info(string message)
        {
            Log("Info", message);
        }

        /// <inheritdoc />
        public void Info(string format, params object[] formatArgs)
        {
            Info(string.Format(CultureInfo.InvariantCulture, format, formatArgs));
        }

        /// <inheritdoc />
        public void Warning(string message)
        {
            Log("Warning", message);
        }

        /// <inheritdoc />
        public void Warning(string format, params object[] formatArgs)
        {
            Warning(string.Format(CultureInfo.InvariantCulture, format, formatArgs));
        }

        /// <inheritdoc />
        public void Warning(Exception exception, string message)
        {
            Warning(message + " " + exception);
        }

        /// <inheritdoc />
        public abstract void Dispose();
    }
}

