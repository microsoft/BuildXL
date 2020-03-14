// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.Instrumentation.Common
{
    /// <summary>
    /// Event that is code generated
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class LoggingDetailsAttribute : Attribute
    {
        /// <summary>
        /// The Name for the logger. This is used to generate the class and interface names
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Wheter to use instance based logging for the generated methods.
        /// </summary>
        public bool InstanceBasedLogging { get; set; }

        /// <summary>
        /// Wheter the codegen should emit debugging information for the proper logger instances
        /// </summary>
        /// <remarks>
        /// Only applicable when <see cref="InstanceBasedLogging" /> is turned on.
        /// This is usefull when debugging if logger instances are not set properly.
        /// </remarks>
        public bool EmitDebuggingInfo { get; set; }

        /// <summary>
        /// Constructor that takes the loggers name
        /// </summary>
        public LoggingDetailsAttribute(string name)
        {
            Name = name;
        }
    }
}
