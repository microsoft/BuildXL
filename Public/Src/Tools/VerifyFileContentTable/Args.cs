// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.ToolSupport;
using BuildXL.Utilities;

namespace Tool.VerifyFileContentTable
{
    /// <summary>
    /// Argument processor for the file content table verifier
    /// </summary>
    public sealed class Args : CommandLineUtilities
    {
        private bool m_helpRequested;
        private bool m_noLogo;
        private readonly string m_fileContentTablePath;

        /// <summary>
        /// Constructor.
        /// </summary>
        public Args(string[] args)
            : base(args)
        {
            Contract.Requires(args != null);

            foreach (Option opt in Options)
            {
                Contract.Assume(opt.Name != null);
                switch (opt.Name.ToUpperInvariant())
                {
                    case "?":
                        HandleHelpOption(opt);
                        break;
                    case "NOLOGO":
                        HandleNologoOption(opt);
                        break;
                    default:
                        Contract.Assume(!string.IsNullOrEmpty(Resources.Args_NotRecognized));
                        throw Error(Resources.Args_NotRecognized, opt.Name);
                }
            }

            if (!HelpRequested)
            {
                string[] arguments = Arguments.ToArray();
                if (arguments.Length != 1)
                {
                    Contract.Assume(!string.IsNullOrEmpty(Resources.Args_Expected_file_content_table_path));
                    throw Error(Resources.Args_Expected_file_content_table_path);
                }

                m_fileContentTablePath = arguments[0];
            }
        }

        private void HandleNologoOption(Option opt)
        {
            ParseVoidOption(opt);
            m_noLogo = true;
        }

        private void HandleHelpOption(Option opt)
        {
            ParseVoidOption(opt);
            m_helpRequested = true;
        }

        /// <summary>
        /// If true, shows help.
        /// </summary>
        public bool HelpRequested
        {
            get { return m_helpRequested; }
        }

        /// <summary>
        /// If true, do not show logo and copyright.
        /// </summary>
        public bool NoLogo
        {
            get { return m_noLogo; }
        }

        /// <summary>
        /// Path to the file content table to verify.
        /// </summary>
        public string FileContentTablePath
        {
            get { return m_fileContentTablePath; }
        }

        /// <summary>
        /// Acquires arguments.
        /// </summary>
        public static Args Acquire(string[] args)
        {
            Contract.Requires(args != null);

            try
            {
                return new Args(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                return null;
            }
        }
    }
}
