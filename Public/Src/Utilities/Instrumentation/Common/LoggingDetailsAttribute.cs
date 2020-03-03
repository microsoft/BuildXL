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
        /// Constructor that takes the loggers name
        /// </summary>
        public LoggingDetailsAttribute(string name)
        {
            Name = name;
        }
    }
}
