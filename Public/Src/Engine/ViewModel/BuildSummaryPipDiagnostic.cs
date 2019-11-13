// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

namespace BuildXL.ViewModel
{
    /// <nodoc />
    public class BuildSummaryPipDiagnostic
    {
        /// <nodoc />
        public string SemiStablePipId { get; set; }

        /// <nodoc />
        public string PipDescription { get; set; }

        /// <nodoc />
        public string SpecPath { get; set; }

        /// <nodoc />
        public string ToolName { get; set; }

        /// <nodoc />
        public int ExitCode { get; set; }

        /// <nodoc />
        public string Output { get; set; }

        /// <nodoc />
        internal void RenderMarkDown(MarkDownWriter writer)
        {
            writer.WriteRaw("### <span style=\"font - family:consolas; monospace\">");
            writer.WriteText(PipDescription);
            writer.WriteRaw("</span> failed with exit code ");
            writer.WriteLineRaw(ExitCode.ToString(CultureInfo.InvariantCulture));

            writer.StartDetails("Pip Details");
            writer.StartTable();
            WriteKeyValuePair(writer, "PipHash:", SemiStablePipId, true);
            WriteKeyValuePair(writer, "Pip:", PipDescription, true);
            WriteKeyValuePair(writer, "Spec:", SpecPath, true);
            WriteKeyValuePair(writer, "Tool:", ToolName, true);
            WriteKeyValuePair(writer, "Exit Code:", ExitCode.ToString(CultureInfo.InvariantCulture), true);
            writer.EndTable();
            writer.EndDetails();

            if (!string.IsNullOrEmpty(Output))
            {
                writer.WritePre(Output);
            }
        }

        private void WriteKeyValuePair(MarkDownWriter writer, string key, string value, bool fixedFont)
        {
            writer.StartElement("tr");

            writer.StartElement("td", "text-align:left;vertical-align: text-top;min-width:5em");
            writer.WriteText(key);
            writer.EndElement("td");

            writer.StartElement("td");
            if (fixedFont)
            {
                writer.StartElement("span", "font-family:consolas;monospace");
            }

            writer.WriteText(value);
            if (fixedFont)
            {
                writer.EndElement("span");
            }
            writer.EndElement("td");
            writer.EndElement("tr");
        }
    }
}
