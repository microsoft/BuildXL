// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using System.Text;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Utilities.Debugging
{
    /// <summary>
    /// Enables debug tracing capabilities, essentially wrapping a string builder and exposing AppendLine, Clear and ToString.
    /// Because creating this struct leases a string builder from the StringBuilder pool, consumers should Dispose it appropriately.
    /// This class is thread safe except for the Dispose method, which should be called without concurrent calls to the logging methods.
    /// </summary>
    /// <remarks>
    /// The point of this abstraction is to to provide a way to add debugging capabilities throughout the code that
    /// will have minimal overhead when in a 'disabled' state. This way, consumers only need to worry about this at "construction" time 
    /// (typically, using an optional setting to enable the debugging in that particular section of the code), and can add arbitrary debugging 
    /// lines in the rest of the logic without worrying about impacting builds that are not meant to be observed with the extra logging.
    /// This is achieved by using a custom InterpolatedStringHandler, which will only construct the interpolated strings when the debugging is enabled.
    /// </remarks>
    public readonly struct DebugTrace : IDisposable
    {
        private readonly PooledObjectWrapper<StringBuilder> m_backingStringBuilder;

        /// <summary>
        /// Creates an empty debugging trace
        /// </summary>
        /// <param name="enabled">When this is false, any operations on this struct are no-ops, and ToString returns the empty string.</param>
        public DebugTrace(bool enabled)
        {
#if !NETCOREAPP
	    m_enabled = false;
	    m_backingStringBuilder = default; // Prevent unused variable warnings
            throw new NotImplementedException($"{nameof(DebugTrace)} should only be used on .NET Core components because its performance relies on .NET Core features.");
#else
            m_enabled = enabled;
            if (enabled)
            {
                m_backingStringBuilder = Pools.GetStringBuilder();
            }
#endif
        }

        /// <nodoc />
        public DebugTrace() => m_enabled = false;

        private readonly StringBuilder Buffer => m_backingStringBuilder.Instance;

        private readonly bool m_enabled;

        /// <summary>
        /// Whether tracing is enabled: when this is false, any operations on this struct are no-ops, and ToString returns the empty string.
        /// </summary>
        public readonly bool Enabled => m_enabled;

        /// <inheritdoc />
        public override readonly string ToString()
        {
            if (!m_enabled)
            {
                return "(disabled DebugTrace)";
            }

            var sb = Buffer;
            lock (sb)
            {
                return sb.ToString();
            }
        }

        /// <inheritdoc />
        public readonly void Dispose()
        {
            if (m_enabled)
            {
                m_backingStringBuilder.Dispose();
            }
        }

#if NETCOREAPP
        /// <nodoc />
        public readonly void AppendLine([InterpolatedStringHandlerArgument("")] DebugTraceInterpolatedStringHandler builder)
        {
            if (m_enabled)
            {
                var s = builder.GetFormattedString();
                var sb = Buffer;
                lock (sb)
                {
                    sb.AppendLine(s);
                }
            }
        }
#endif

        /// <nodoc />
        public readonly void AppendLine(string s)
        {
            if (m_enabled)
            {
                var sb = Buffer;
                lock (sb)
                {
                    sb.AppendLine(s);
                }
            }
        }

        /// <summary>
        /// Clears this trace, returning to an empty state. 
        /// If this tracer is disabled, it stays disabled.
        /// </summary>
        public void Clear()
        {
            if (m_enabled)
            {
                var sb = Buffer;
                lock (sb)
                {
                    Buffer.Clear();
                }
            }
        }
    }

#if NETCOREAPP
    /// <summary>
    /// String handler to avoid constructing strings that we won't log outside of debug mode
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct DebugTraceInterpolatedStringHandler
    {
        private DefaultInterpolatedStringHandler m_handler;
        /// <nodoc />
        public DebugTraceInterpolatedStringHandler(int literalLength, int formattedCount, DebugTrace caller, out bool handlerIsValid)
        {
            if (!caller.Enabled)
            {
                m_handler = default;
                handlerIsValid = false;
                return;
            }

            m_handler = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
            handlerIsValid = true;
        }

        /// <nodoc />
        public void AppendLiteral(string s) => m_handler.AppendLiteral(s);

        /// <nodoc />
        public void AppendFormatted<T>(T t) => m_handler.AppendFormatted(t);

        /// <nodoc />
        public string GetFormattedString() => m_handler.ToStringAndClear();
    }
#endif
}