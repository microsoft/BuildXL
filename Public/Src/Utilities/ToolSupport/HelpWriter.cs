// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Text;
using BuildXL.Utilities.Tracing;

namespace BuildXL.ToolSupport
{
    /// <summary>
    /// Utility class to output properly formatted help text.
    /// </summary>
    /// <remarks>
    /// This class formats text to fit to the current size of the console window. It supports
    /// banners and options. Banners are titles displayed between groups of options. Options
    /// are represented by pair, where the first element is the short name for the option while
    /// the section element is the long description for the option.
    /// </remarks>
    public sealed class HelpWriter
    {
        private const int DescriptionColumn = 32; // character offset where the description text starts for an option

        private readonly StringBuilder m_builder = new StringBuilder();
        private readonly TextWriter m_writer = Console.Out;
        private readonly int m_characterWidth = GetConsoleBufferWidth();
        private readonly HelpLevel m_requestedLevel;

        /// <summary>
        /// Creates an instance of this object that outputs to the console.
        /// </summary>
        public HelpWriter(HelpLevel requestedLevel = HelpLevel.Standard)
        {
            m_requestedLevel = requestedLevel;
        }

        /// <summary>
        /// Creates an instance of this object that outputs to the specified <see cref="TextWriter" />.
        /// </summary>
        /// <param name="writer">Where to send the output to.</param>
        /// <param name="characterWidth">The number of characters after which to perform word wrapping.</param>
        /// <param name="requestedLevel">The level of help being requested. Defaults to Standard</param>
        public HelpWriter(TextWriter writer, int characterWidth, HelpLevel requestedLevel = HelpLevel.Standard)
        {
            Contract.Requires(writer != null);

            m_writer = writer;
            m_characterWidth = characterWidth;
            m_requestedLevel = requestedLevel;
        }

        /// <summary>
        /// Wrapper to get the consoles buffer width.
        /// </summary>
        private static int GetConsoleBufferWidth()
        {
            return StandardConsole.GetConsoleWidth();
        }

        /// <summary>
        /// Writes a word-wrapped line to the console with a specific wrapping column.
        /// </summary>
        private void WriteLine(string line, int wrapColumn)
        {
            Contract.Requires(line != null);
            Contract.Requires(wrapColumn >= 0);

            string[] words = line.Split(' ');

            foreach (string word in words)
            {
                if (m_builder.Length < wrapColumn)
                {
                    m_builder.Append(' ', wrapColumn - m_builder.Length);
                }
                else if (m_builder.Length + 1 + word.Length >= m_characterWidth)
                {
                    m_writer.WriteLine(m_builder.ToString());
                    m_builder.Length = 0;
                    m_builder.Append(' ', wrapColumn);
                }
                else if (m_builder.Length > 0)
                {
                    m_builder.Append(' ');
                }

                m_builder.Append(word);
            }

            m_writer.WriteLine(m_builder.ToString());
        }

        /// <summary>
        /// Writes a blank line to the console.
        /// </summary>
        public void WriteLine(HelpLevel level = HelpLevel.Standard)
        {
            if (m_requestedLevel < level)
            {
                return;
            }

            m_writer.WriteLine();
        }

        /// <summary>
        /// Writes a word-wrapped line to the console.
        /// </summary>
        public void WriteLine(string line, HelpLevel level = HelpLevel.Standard)
        {
            Contract.Requires(line != null);

            if (m_requestedLevel < level)
            {
                return;
            }

            m_builder.Length = 0;
            WriteLine(line, 0);
        }

        /// <summary>
        /// Writes an option and its description.
        /// </summary>
        public void WriteOption(string name, string description, HelpLevel level = HelpLevel.Standard, string shortName = null)
        {
            Contract.Requires(name != null);
            Contract.Requires(description != null);

            if (m_requestedLevel < level)
            {
                return;
            }

            if (shortName != null)
            {
                var shortForm = string.Format(CultureInfo.InvariantCulture, "(Short form: /{0})", shortName);
                if (string.IsNullOrEmpty(description))
                {
                    description = shortForm;
                }
                else
                {
                    description += " " + shortForm;
                }
            }

            if (string.IsNullOrEmpty(description))
            {
                m_writer.WriteLine(name);
                return;
            }

            m_builder.Length = 0;
            if (name.Length >= DescriptionColumn)
            {
                m_writer.WriteLine(name);
            }
            else
            {
                m_builder.Append(name);
            }

            WriteLine(description, DescriptionColumn);
        }

        /// <summary>
        /// Writes a centered banner that separates groups of options.
        /// </summary>
        public void WriteBanner(string banner, HelpLevel level = HelpLevel.Standard)
        {
            Contract.Requires(banner != null);

            if (m_requestedLevel < level)
            {
                return;
            }

            m_writer.WriteLine();

            string fullBanner = "- " + banner + " -";
            int avail = m_characterWidth;
            if (avail > fullBanner.Length)
            {
                m_builder.Length = 0;
                m_builder.Append(' ', (avail - fullBanner.Length) / 2);
                m_builder.Append(fullBanner);
                m_writer.WriteLine(m_builder.ToString());
                m_builder.Length = 0;
            }
            else
            {
                m_writer.WriteLine(fullBanner);
            }

            m_writer.WriteLine();
        }
    }

    /// <summary>
    /// How much help to show
    /// </summary>
    /// <remarks>
    /// CAUTION!!!
    /// This is a duplication from HelpText in BuildXL.Utilities.Configuration for the sake of not adding dependencies. Make sure to
    /// keep it in sync if it is modified
    /// </remarks>
    public enum HelpLevel : byte
    {
        /// <summary>
        /// None
        /// </summary>
        None = 0,

        /// <summary>
        /// The standard help
        /// </summary>
        Standard = 1,

        /// <summary>
        /// The full help including verbose obscure options
        /// </summary>
        Verbose = 2,
    }
}
