// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;
using System.IO;
using System.Net;

namespace BuildXL.ViewModel
{
    /// <nodoc />
    internal class MarkDownWriter : IDisposable
    {
        private TextWriter m_writer;

        /// <nodoc />
        public MarkDownWriter(string filePath)
        {
            m_writer = new StreamWriter(filePath);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_writer.Dispose();
        }

        /// <nodoc />
        public void WriteHeader(string header)
        {
            m_writer.WriteLine();
            m_writer.Write("## ");
            m_writer.WriteLine(header);
        }

        //
        // Detailed table entries
        //

        /// <nodoc />
        public void WriteDetailedTableEntry(string key, string value)
        {
            StartDetailedTableEntry(key);
            m_writer.WriteLine(HtmlEscape(value));
            EndDetailedTableEntry();
        }

        /// <nodoc />
        private void StartDetailedTableEntry(string key)
        {
            m_writer.WriteLine("<tr>");
            m_writer.WriteLine("<th  style='text-align:left;vertical-align: text-top;width:12em;'>");
            m_writer.WriteLine(HtmlEscape(key));
            m_writer.WriteLine("</th>");
            m_writer.WriteLine("<td>");
        }

        /// <nodoc />
        private void EndDetailedTableEntry()
        {
            m_writer.WriteLine("</td>");
            m_writer.WriteLine("</tr>");
        }
        
        /// <nodoc />
        public void StartDetailedTableSummary(string key, string summary)
        {
            StartDetailedTableEntry(key);
            StartDetails(summary);
        }

        /// <nodoc />
        public void StartDetails(string summary)
        {
            m_writer.WriteLine("<details>");
            m_writer.WriteLine("<summary>");
            m_writer.WriteLine(HtmlEscape(summary));
            m_writer.WriteLine("</summary>");
        }

        /// <nodoc />
        public void EndDetails()
        {
            m_writer.WriteLine("</details>");
        }

        /// <nodoc />
        public void EndDetailedTableSummary()
        {
            EndDetails();
            EndDetailedTableEntry();
        }

        /// <nodoc />
        public void WritePreDetails(string summary, string details, int indentPixels = 0)
        {
            m_writer.Write("<details");
            if (indentPixels > 0)
            {
                m_writer.Write($" style='margin-left:{indentPixels}px'");
            }
            m_writer.WriteLine(">");

            m_writer.WriteLine("<summary>");
            m_writer.WriteLine(HtmlEscape(summary));
            m_writer.WriteLine("</summary>");
            m_writer.WriteLine("<pre>");
            m_writer.WriteLine(HtmlEscape(details));
            m_writer.WriteLine("</pre>");
            m_writer.WriteLine("</summary>");

            m_writer.WriteLine("</details>");
        }

        /// <nodoc />
        public void WritePre(string contents)
        {
            m_writer.WriteLine("<pre>");
            m_writer.WriteLine(HtmlEscape(contents));
            m_writer.WriteLine("</pre>");
        }


        //
        // Generic table writers
        //

        /// <nodoc />
        public void StartTable(params object[] headers)
        {
            m_writer.WriteLine("<table>");
            if (headers?.Length > 0)
            {
                WriteRow(headers, true);
            }
        }

        /// <nodoc />
        public void WriteTableRow(params object[] columns)
        {
            WriteRow(columns, false);
        }

        private void WriteRow(object[] columns, bool isHeader)
        {
            var rowChar = isHeader ? 'h' : 'd';

            m_writer.WriteLine("<tr>");
            foreach (var column in columns)
            {
                m_writer.Write($"<t{rowChar}>");
                m_writer.Write(HtmlEscape(Convert.ToString(column, CultureInfo.InvariantCulture)));
                m_writer.Write($"</t{rowChar}>");
            }
            m_writer.WriteLine("</tr>");

        }

        /// <nodoc />
        public void EndTable()
        {
            m_writer.WriteLine("</table>");
        }

        //
        // Basic helpers
        //

        /// <nodoc />
        public void StartElement(string element, string style = null)
        {
            m_writer.WriteLine("<" + element +  (style == null ? null : " style=\"" + style + "\"") + ">");
        }

        /// <nodoc />
        public void EndElement(string element)
        {
            m_writer.WriteLine("</" + element + ">");
        }

        /// <nodoc />
        public void WriteRaw(string text)
        {
            m_writer.Write(text);
        }

        /// <nodoc />
        public void WriteText(string text)
        {
            m_writer.Write(HtmlEscape(text));
        }

        /// <nodoc />
        public void WriteLineRaw(string text)
        {
            m_writer.WriteLine(text);
        }

        /// <nodoc />
        private string HtmlEscape(string value)
        {
            return WebUtility.HtmlEncode(value);
        }
    }
}
