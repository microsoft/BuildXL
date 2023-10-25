// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.CompilerServices;

namespace BuildXL.Processes
{

#if NETCOREAPP
    /// <summary>
    /// String handler to avoid constructing strings that we won't log outside of sandbox debug mode
    /// </summary>
    [InterpolatedStringHandler]
    internal ref struct DebugMessageInterpolatedStringHandler
    {
        private readonly DefaultInterpolatedStringHandler m_handler;

        // We need constructors for every possible object that might use the handler with an interpolated string

        /// <nodoc />
        public DebugMessageInterpolatedStringHandler(int literalLength, int formattedCount, SandboxConnectionLinuxDetours.Info caller, out bool handlerIsValid)
            : this(literalLength, formattedCount, caller.Process, out handlerIsValid)
        {
        }

        /// <nodoc />
        public DebugMessageInterpolatedStringHandler(int literalLength, int formattedCount, SandboxConnectionLinuxDetours.Info.ReportProcessor caller, out bool handlerIsValid)
        : this(literalLength, formattedCount, caller.Info.Process, out handlerIsValid)
        {
        }

        /// <nodoc />
        public DebugMessageInterpolatedStringHandler(int literalLength, int formattedCount, UnsandboxedProcess caller, out bool handlerIsValid)
        {
            m_handler = default;

            if (!caller.DebugLogEnabled)
            {
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

        internal string GetFormattedString() => m_handler.ToString();
    }
#endif
}
