// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using JetBrains.Annotations;

#pragma warning disable 1709

namespace BuildXL.Utilities.CodeGenerationHelper
{
    public partial class CodeGenerator
    {
        private readonly Debracer m_debracer;
        private readonly Deindenter m_deindenter;
        private Action<char> m_output;
        private int m_indentLevel;
        private bool m_needIndent = true;

        /// <summary>
        /// Creates an indented scope surrounded by braces.
        /// </summary>
        public IDisposable Br
        {
            get
            {
                Ln("{");
                m_indentLevel++;
                return m_debracer;
            }
        }

        /// <summary>
        /// Creates an indented scope surrounded by braces and terminated with a comma.
        /// </summary>
        public IDisposable BrC
        {
            get
            {
                Ln("{");
                m_indentLevel++;
                return new DebracerStatement(this, ",");
            }
        }

        /// <summary>
        /// Creates an indented scope surrounded by braces and terminated with a semicolon.
        /// </summary>
        public IDisposable BrS
        {
            get
            {
                Ln("{");
                m_indentLevel++;
                return new DebracerStatement(this, ";");
            }
        }

        /// <summary>
        /// Creates an indented scope surrounded by braces and terminated with a parenthesis and semicolon.
        /// </summary>
        public IDisposable BrPS
        {
            get
            {
                Ln("{");
                m_indentLevel++;
                return new DebracerStatement(this, ");");
            }
        }

        /// <summary>
        /// Creates an indented scope surrounded by braces and terminated with 2 parens + semicolon.
        /// </summary>
        /// <remarks>
        /// This might be used to generate code of the following form:
        /// <![CDATA[
        ///         var x = SomeMethodWithDelegate(() => {
        ///             ...
        ///         }));
        /// ]]>
        /// </remarks>
        public IDisposable BrPPS
        {
            get
            {
                Ln("{");
                m_indentLevel++;
                return new DebracerStatement(this, "));");
            }
        }

        /// <summary>
        /// Adds if Debug directive.
        /// </summary>
        public IDisposable IfDebug
        {
            get
            {
                Ln("#if DEBUG");
                return new EndWrappedPreprocessor(this, "endif");
            }
        }

        /// <summary>
        /// Creates an indented scope.
        /// </summary>
        public IDisposable Indent
        {
            get
            {
                m_indentLevel++;
                return m_deindenter;
            }
        }

        private void Format(string compositeFormat, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(compositeFormat));
            Contract.Requires(args != null);

            Output(string.Format(CultureInfo.InvariantCulture, compositeFormat, args));
        }

        private void Output(string str)
        {
            if (str == null)
            {
                return;
            }

            foreach (char ch in str)
            {
                if (m_needIndent)
                {
                    m_needIndent = false;
                    for (int i = 0; i < m_indentLevel; i++)
                    {
                        m_output(' ');
                        m_output(' ');
                        m_output(' ');
                        m_output(' ');
                    }
                }

                if (ch == '\n')
                {
                    m_output('\r');
                    m_needIndent = true;
                }

                if (ch == '\r')
                {
                    continue;
                }

                m_output(ch);
            }
        }

        /// <summary>
        /// Outputs a character
        /// </summary>
        public void Output(char c)
        {
            m_output(c);
        }

        /// <summary>
        /// Outputs a new line.
        /// </summary>
        public void Ln()
        {
            Output("\n");
        }

        /// <summary>
        /// Output a new format string line if a condition holds true
        /// </summary>
        /// <param name="condition">condition used to determine if line should be written</param>
        /// <param name="format">format string</param>
        /// <param name="args">args of format string</param>
        [StringFormatMethod("format")]
        public void Ln(bool condition, string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);
            if (condition)
            {
                Ln(format, args);
            }
        }

        /// <summary>
        /// Output a new format string line.
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">args of format string</param>
        [StringFormatMethod("format")]
        public void Ln(string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);

            StringFormat(format, args);
            Ln();
        }

        /// <summary>
        /// Output a new format string line ended with semicolon.
        /// </summary>
        /// <param name="format">format string</param>
        /// <param name="args">args of format string.</param>
        [StringFormatMethod("format")]
        public void Lns(string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);

            StringFormat(format, args);
            Output(";\n");
        }

        /// <summary>
        /// Renders an indented Line
        /// </summary>
        [StringFormatMethod("format")]
        public void IndentLn(string format, params object[] args)
        {
            using (Indent)
            {
                Ln(format, args);
            }
        }

        /// <summary>
        /// Generates a getter
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "string")]
        [StringFormatMethod("bodyFormatString")]
        public void Get(string bodyFormatString, params object[] args)
        {
            Output("get { ");
            StringFormat(bodyFormatString, args);
            Output(" }");
            Ln();
        }

        /// <summary>
        /// Generates a setter
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "string")]
        [StringFormatMethod("bodyFormatString")]
        public void Set(string bodyFormatString, params object[] args)
        {
            Output("set { ");
            StringFormat(bodyFormatString, args);
            Output(" }");
            Ln();
        }

        private void StringFormat(string format, params object[] args)
        {
            Contract.Requires(!string.IsNullOrEmpty(format));
            Contract.Requires(args != null);

            if (args.Length == 0)
            {
                Output(format);
            }
            else
            {
                Format(format, args);
            }
        }
    }
}
