// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.IO;
using System.Net;

namespace BuildXL.ViewModel
{
    /// <nodoc />
    internal class MarkDownWriter : IDisposable
    {
        private readonly TextWriter m_writer;
        private readonly FileStream m_stream;
        private readonly long m_targetBytes;

        /// <nodoc />
        public MarkDownWriter(string filePath, 
            long targetLengthBytes = 10 * 1024 * 1024 /*Default to 10MB limit to avoid browser perf issues*/)
        {
            m_stream = new FileStream(filePath, FileMode.CreateNew);
            m_writer = new StreamWriter(m_stream);
            m_targetBytes = targetLengthBytes;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            m_writer.Dispose();
        }

        /// <summary>
        /// Length in bytes of markdown file written so far.
        /// </summary>
        public long Length
        {
            get => m_stream.Length;
        }

        /// <summary>
        /// Whether the total length of the markdown exceeds the target size. Useful for callers
        /// to regulate how much data to include
        /// </summary>
        public bool ExceedsTargetBytes
        {
            get => m_targetBytes < Length;
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
        public void WritePreSection(string title, string details, int indentPixels = 0)
        {
            m_writer.Write("<div");
            if (indentPixels > 0)
            {
                m_writer.Write($" style='margin-left:{indentPixels}px'");
            }
            m_writer.WriteLine(">");

            m_writer.WriteLine("<h3>");
            m_writer.WriteLine(HtmlEscape(title));
            m_writer.WriteLine("</h3>");
            m_writer.WriteLine("<div>");
            m_writer.WriteLine("<pre>");
            m_writer.WriteLine(HtmlEscape(details));
            m_writer.WriteLine("</pre>");
            m_writer.WriteLine("</div>");

            m_writer.WriteLine("</div>");
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
