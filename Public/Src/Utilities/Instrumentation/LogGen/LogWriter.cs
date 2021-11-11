// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities.CodeGenerationHelper;
using BuildXL.LogGen.Core;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Writes the generated loggers to implement the partial class
    /// </summary>
    internal sealed class LogWriter
    {
        private const string GlobalInstrumentationNamespace = "global::BuildXL.Utilities.Instrumentation.Common";
        private const string NotifyContextWhenErrorsAreLogged = "m_notifyContextWhenErrorsAreLogged";
        private const string NotifyContextWhenWarningsAreLogged = "m_notifyContextWhenWarningsAreLogged";

        private readonly string m_path;
        private readonly string m_namespace;
        private readonly string m_targetFramework;
        private readonly string m_targetRuntime;
        private readonly ErrorReport m_errorReport;

        private List<GeneratorBase> m_generators;

        /// <nodoc />
        public LogWriter(Configuration config, ErrorReport errorReport)
        {
            m_path = config.OutputCSharpFile;
            m_namespace = config.Namespace;
            m_errorReport = errorReport;
            m_targetFramework = config.TargetFramework;
            m_targetRuntime = config.TargetRuntime;
        }

        /// <summary>
        /// Writes the log file
        /// </summary>
        public int WriteLog(IReadOnlyList<LoggingClass> loggingClasses)
        {
            var itemsWritten = 0;
            using (var fs = File.Open(m_path, FileMode.Create))
            using (StreamWriter writer = new StreamWriter(fs))
            {
                CodeGenerator gen = new CodeGenerator((c) => writer.Write(c));
                itemsWritten = LogWriterHelpers.WriteLogToStream(loggingClasses, gen, m_namespace, m_targetFramework, m_targetRuntime, ref m_generators, m_errorReport);
            }

            return itemsWritten;
        }
    }
}
