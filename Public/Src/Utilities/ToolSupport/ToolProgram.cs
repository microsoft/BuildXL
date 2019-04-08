// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Base class for common console apps
    /// </summary>
    /// <remarks>
    /// This class provides the common functionality for console apps.
    ///
    /// Derived types should write:
    ///     public class MyProgram : ToolProgram...
    ///     {
    ///         public static int Main(string[] args)
    ///         {
    ///             return new MyProgram().MainImpl(args);
    ///         }
    ///
    ///         public override bool TryParse(out IArguments args)
    ///         {
    ///             // Parse args
    ///             return true;
    ///         }
    ///
    ///         public override bool Run(IArguments args)
    ///         {
    ///             // Do Stuff
    ///             return true;
    ///         }
    ///     }
    ///
    /// </remarks>
    public abstract class ToolProgram<TArguments>
    {
        private readonly string m_programName;

        private string[] m_rawArgs;

        /// <summary>
        /// Protected constructor to control default behavior
        /// </summary>
        protected ToolProgram(string programName)
        {
            m_programName = programName;
        }

        /// <summary>
        /// The common implementation of the Main method.
        /// </summary>
        protected virtual int MainHandler(string[] args)
        {
            if (m_programName != null)
            {
                if (Environment.GetEnvironmentVariable(m_programName + "DebugOnStart") == "1")
                {
                    Debugger.Launch();
                }
            }

            TArguments arguments;
            if (!TryParse(args, out arguments))
            {
                return 1;
            }

            m_rawArgs = args;

            // Note that we do not wrap Run in a catch-all exception handler. If we did, then last-chance handling (i.e., an 'unhandled exception'
            // event) is neutered - but only for the main thread! Instead, we want to have a uniform last-chance handling method that does the
            // right telemetry / Windows Error Reporting magic as part of crashing (possibly into a debugger).
            // TODO: Promote the last-chance handler from BuildXLApp to here?
            return Run(arguments);
        }

        /// <summary>
        /// Attempts to parse the strongly typed arguments from the raw arguments
        /// </summary>
        /// <param name="rawArgs">the raw arguments array passed to the program</param>
        /// <param name="arguments">the strongly typed arguments</param>
        /// <returns>true, if the arguments were successfully parsed. Otherwise, false.</returns>
        public abstract bool TryParse(string[] rawArgs, out TArguments arguments);

        /// <summary>
        /// The core execution of the tool.
        /// </summary>
        /// <remarks>
        /// If you discover boilerplate in multiple implementations, add it to MainImpl, or add another inheritance hierarchy.
        /// </remarks>
        public abstract int Run(TArguments arguments);

        /// <summary>
        /// In case one needs access to the original arguments.
        /// </summary>
        protected IReadOnlyCollection<string> RawArgs
        {
            get { return m_rawArgs; }
        }
    }
}
