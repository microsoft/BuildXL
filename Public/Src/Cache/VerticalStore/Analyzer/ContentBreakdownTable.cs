// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// This class is used to represent content statistics for a session.
    /// </summary>
    public sealed class ContentBreakdownTable
    {
        /// <summary>
        /// Specifies the sizes of content in this session.
        /// </summary>
        public ReadOnlyDictionary<CasHash, long> Breakdown { get; private set; }

        /// <summary>
        /// Specifies the number of rows in the content table.
        /// </summary>
        public int Count => Breakdown.Count;

        /// <summary>
        /// Specifies the raw size values in the content table.
        /// </summary>
        public IEnumerable<long> Sizes => Breakdown.Values;

        /// <summary>
        /// Initializes a new instance of the ContentBreakdownTable class.
        /// </summary>
        /// <param name="breakdown">Supplies an <![CDATA[Dictionary<CasHash, long>]]> of content sizes.</param>
        public ContentBreakdownTable(Dictionary<CasHash, long> breakdown)
        {
            Breakdown = new ReadOnlyDictionary<CasHash, long>(breakdown);
        }

        /// <summary>
        /// Writes the breakdown table to a CSV.
        /// </summary>
        /// <param name="filename">Supplies the output file name. The ".csv" extension is added automatically.</param>
        /// <param name="maxRowsPerFile">Supplies the maximum number of rows to write to the file</param>
        /// <remarks>
        /// If the CSV results in more than 1,000,000 rows, the CSV will be broken into
        /// multiple pieces, each of the form "filename.0.csv, filename.1.csv, etc".
        /// </remarks>
        public void WriteCSV(string filename, int maxRowsPerFile = 1000000)
        {
            const string header = "Hash,Size";
            var orderedBreakdown = Breakdown.OrderByDescending((e) => e.Value);

            // Subtract room for the header
            if (maxRowsPerFile <= 1)
            {
                throw new ArgumentException(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} ({1}) must be larger than 1", nameof(maxRowsPerFile), maxRowsPerFile));
            }

            maxRowsPerFile -= 1;

            // Blast the "csv" extension if supplied, because we'll add it back (or something else) below.
            if (filename.EndsWith(".csv"))
            {
                filename = filename.Substring(0, filename.Length - 4);
            }

            if (Breakdown.Count <= maxRowsPerFile)
            {
                // This is the easy case. Just spew to the output file.
                using (var fs = new FileStream(filename + ".csv", FileMode.Create))
                {
                    using (var sw = new StreamWriter(fs))
                    {
                        sw.WriteLine(header);
                        foreach (KeyValuePair<CasHash, long> kvp in orderedBreakdown)
                        {
                            sw.WriteLine("{0},{1}", kvp.Key, kvp.Value);
                        }
                    }
                }
            }
            else
            {
                // This is the annoying case. Get the raw enumeration and dump it out
                // maxRowsPerFile at a time.
                var enumerator = orderedBreakdown.GetEnumerator();
                int chunkBase = 0;
                int chunkIndex = 0;
                while (chunkBase + chunkIndex < Breakdown.Count)
                {
                    string chunkFilename = string.Format("{0}.{1}.csv", filename, chunkBase / maxRowsPerFile);
                    using (var fs = new FileStream(chunkFilename, FileMode.Create))
                    {
                        using (var sw = new StreamWriter(fs))
                        {
                            sw.WriteLine(header);
                            while (chunkIndex < maxRowsPerFile && enumerator.MoveNext())
                            {
                                KeyValuePair<CasHash, long> kvp = enumerator.Current;
                                sw.WriteLine("{0},{1}", kvp.Key, kvp.Value);
                                ++chunkIndex;
                            }
                        }
                    }

                    chunkBase += chunkIndex;
                    chunkIndex = 0;
                }
            }
        }
    }
}
