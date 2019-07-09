// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL;
using BuildXL.Storage;
using BuildXL.Storage.Diagnostics;
using BuildXL.Storage.FileContentTableAccessor;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace Tool.VerifyFileContentTable
{
    /// <summary>
    /// Main program.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Main method.
        /// </summary>
        public static int Main(string[] args)
        {
            Contract.Assert(args != null);

            Args a = Args.Acquire(args);
            if (a == null)
            {
                HelpText.DisplayHelp();
                return 1;
            }

            if (!a.NoLogo)
            {
                HelpText.DisplayLogo();
            }

            if (a.HelpRequested)
            {
                HelpText.DisplayHelp();
                return 1;
            }

            return Run(a) ? 0 : 1;
        }

        /// <summary>
        /// Runs the verifier
        /// </summary>
        public static bool Run(Args args)
        {
            Contract.Requires(args != null);
            Contract.Requires(!args.HelpRequested);

            Contract.Assume(args.FileContentTablePath != null); // Should be provable from the invariant on Args

            DateTime now = DateTime.UtcNow;
            using (ConfigureConsoleLogging(now))
            {
                var pt = new PathTable();
                AbsolutePath path;
                Contract.Assert(args.FileContentTablePath != null, "Implied when help was not requested");
                if (!AbsolutePath.TryCreate(pt, Path.GetFullPath(args.FileContentTablePath), out path))
                {
                    Console.Error.WriteLine(Resources.Bad_table_path, args.FileContentTablePath);
                    return false;
                }

                Console.WriteLine(Resources.Verifying_path, path.ToString(pt));

                FileContentTable tableToVerify = TryLoadFileContentTable(pt, path).Result;
                if (tableToVerify == null)
                {
                    // Note the error has already been logged via TryLoadFileContentTable
                    return false;
                }

                if (!FileContentTableAccessorFactory.TryCreate(out IFileContentTableAccessor accessor, out string error))
                {
                    Console.Error.WriteLine(error);
                    return false;
                }

                Stopwatch sw = Stopwatch.StartNew();
                List<FileContentTableDiagnosticExtensions.IncorrectFileContentEntry> incorrectEntries = null;

                using (accessor)
                {
                    incorrectEntries = tableToVerify.FindIncorrectEntries(accessor);
                }

                sw.Stop();

                foreach (FileContentTableDiagnosticExtensions.IncorrectFileContentEntry incorrectEntry in incorrectEntries)
                {
                    Console.Error.WriteLine(
                        Resources.Incorrect_entry,
                        incorrectEntry.Path,
                        incorrectEntry.ExpectedHash.ToHex(),
                        incorrectEntry.ActualHash.ToHex(),
                        incorrectEntry.Usn);
                }

                Console.WriteLine(Resources.Verification_summary, tableToVerify.Count, incorrectEntries.Count, sw.Elapsed);

                return incorrectEntries.Count == 0;
            }
        }

        private static async Task<FileContentTable> TryLoadFileContentTable(PathTable pt, AbsolutePath path)
        {
            try
            {
                return await FileContentTable.LoadAsync(path.ToString(pt));
            }
            catch (BuildXLException ex)
            {
                Console.Error.WriteLine(Resources.Failed_to_load_table, path.ToString(pt), ex.LogEventMessage);
                return null;
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static EventListener ConfigureConsoleLogging(DateTime baseTime)
        {
            return new ConsoleEventListener(Events.Log, baseTime, colorize: true, animateTaskbar: false, level: EventLevel.Informational, updatingConsole: false, useCustomPipDescription: false);
        }
    }
}
