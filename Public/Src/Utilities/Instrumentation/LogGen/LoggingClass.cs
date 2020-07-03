// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Logging class that contains the logging sites
    /// </summary>
    internal sealed class LoggingClass
    {
        /// <nodoc />
        public ISymbol Symbol { get; }

        /// <summary>
        /// The unique name for the loggers to generate
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The unique name for the interface.
        /// </summary>
        public string InterfaceName => "I" + Name;

        /// <summary>
        /// Wheter to use instance based logging for the generated methods.
        /// </summary>
        public bool InstanceBasedLogging { get; }

        /// <summary>
        /// Wheter the codegen should emit debugging information for the proper logger instances
        /// </summary>
        public bool EmitDebuggingInfo { get; }

        /// <nodoc />
        public IList<LoggingSite> Sites { get; } = new List<LoggingSite>();

        /// <nodoc />
        public LoggingClass(LoggingDetailsAttribute loggingDetailsAttribute, ISymbol symbol)
        {
            Name = loggingDetailsAttribute.Name;
            InstanceBasedLogging = loggingDetailsAttribute.InstanceBasedLogging;
            EmitDebuggingInfo = loggingDetailsAttribute.EmitDebuggingInfo;
            Symbol = symbol;
        }
    }
}
