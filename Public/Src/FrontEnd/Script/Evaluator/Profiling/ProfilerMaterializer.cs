// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Evaluator.Profiling
{
    /// <summary>
    /// Produces a TSV file that contains a list of profiled entries
    /// </summary>
    public sealed class ProfilerMaterializer
    {
        private readonly PathTable m_pathTable;

        /// <nodoc/>
        public ProfilerMaterializer(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
        }

        /// <summary>
        /// Materializes a collection of entries to the specified destination
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the file write fails in a recoverable manner.
        /// </exception>
        public void Materialize(IReadOnlyCollection<ProfiledFunctionCall> entries, AbsolutePath destination)
        {
            Contract.Requires(destination.IsValid);

            var destinationPath = destination.ToString(m_pathTable);

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    // Build the report content. Materializes to the file on a per line basis since the whole content may be very big.
                    using (var file = File.Create(destinationPath))
                    {
                        var stream = new StreamWriter(file);
                        stream.WriteLine(GetHeader());

                        foreach (var entry in entries)
                        {
                            stream.WriteLine(GetLine(entry));
                        }

                        stream.Flush();
                    }
                },
                ex =>
                {
                    throw new BuildXLException("Error while producing the profiler report. Inner exception reason: " + ex.Message, ex);
                });
        }

        private static string GetHeader()
        {
            return "Callsite Name\tDuration(ms)\tCallsite Location\tCurrent Qualifier\tFunction Id\tFunction Name\tFunction Location";
        }

        private string GetLine(ProfiledFunctionCall entry)
        {
            var result = string.Format(
                CultureInfo.InvariantCulture,
                "{0}\t{1}\t{2}\t{3}\t{4}\t{5}\t{6}",
                entry.CallsiteInvocation,
                entry.DurationInclusive,
                entry.CallsiteLocation.ToString(m_pathTable),
                entry.Qualifier,
                entry.FunctionId,
                entry.FunctionName,
                entry.FunctionLocation);

            return result;
        }
    }
}
