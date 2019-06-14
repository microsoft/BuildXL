// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Displays a table with aligned text
    /// </summary>
    internal sealed class DisplayTable<TEnum>
        where TEnum : struct
    {
        private readonly int[] m_maxColumnLengths;
        private readonly List<string[]> m_rows = new List<string[]>();
        private string[] m_currentRow;
        private readonly string m_columnDelimeter;

        public DisplayTable(string columnDelimeter, bool defaultHeader = true)
        {
            m_maxColumnLengths = new int[EnumTraits<TEnum>.ValueCount];
            m_columnDelimeter = columnDelimeter;

            if (defaultHeader)
            {
                NextRow();
                foreach (var value in EnumTraits<TEnum>.EnumerateValues())
                {
                    Set(value, value.ToString());
                }
            }
        }

        public void NextRow()
        {
            m_currentRow = new string[EnumTraits<TEnum>.ValueCount];
            m_rows.Add(m_currentRow);
        }

        public void Set(TEnum column, object value)
        {
            if (value == null)
            {
                return;
            }

            var stringValue = value.ToString();
            var columnIndex = EnumTraits<TEnum>.ToInteger(column);
            m_maxColumnLengths[columnIndex] = Math.Max(m_maxColumnLengths[columnIndex], stringValue.Length);
            m_currentRow[columnIndex] = stringValue;
        }

        public void Write(TextWriter writer)
        {
            StringBuilder sb = new StringBuilder();

            var buffer = new char[m_maxColumnLengths.Sum() + (m_columnDelimeter.Length * (EnumTraits<TEnum>.ValueCount - 1))];
            foreach (var row in m_rows)
            {
                sb.Clear();

                for (int i = 0; i < row.Length; i++)
                {
                    var value = row[i] ?? string.Empty;
                    sb.Append(' ', m_maxColumnLengths[i] - value.Length);
                    sb.Append(value);
                    if (i != (row.Length - 1))
                    {
                        sb.Append(m_columnDelimeter);
                    }
                }

                sb.CopyTo(0, buffer, 0, buffer.Length);
                writer.Write(buffer, 0, buffer.Length);

                if (row != m_currentRow)
                {
                    writer.WriteLine();
                }
            }
        }
    }
}
