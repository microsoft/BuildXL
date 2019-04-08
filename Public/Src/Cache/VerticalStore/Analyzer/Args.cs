// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.VerticalAggregator;
using BuildXL.ToolSupport;
using BuildXL.Utilities;

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// This class is responsible for the command line interface
    /// to the program
    /// </summary>
    internal sealed class Args : CommandLineUtilities, IDisposable
    {
        private static readonly string[] s_helpStrings = new[] { "?", "help" };

        public readonly bool Help;

        /// <summary>
        /// Json config string for the cache the user wishes
        /// to check
        /// </summary>
        private string m_jsonString;

        /// <summary>
        /// If true, a statistical analysis of the cache
        /// will be performed.
        /// </summary>
        private bool m_runStatisticalAnalysis;

        /// <summary>
        /// If true, statistical analysis will include
        /// content size for the session. This may be
        /// preposterously slow.
        /// </summary>
        private bool m_analyzeContent;

        /// <summary>
        /// If true, a consistency check of the cache will
        /// be performed.
        /// </summary>
        private bool m_runConsistencyCheck;

        /// <summary>
        /// If true, the sizes of all input assertion lists
        /// associated with a particular weak fingerprint
        /// will be compared to find large discrepancies.
        /// </summary>
        private bool m_findInputAssertionListAnomalies;

        /// <summary>
        /// If true, every strong fingerprint found will have its input
        /// assertion list dumped.
        /// </summary>
        private bool m_dumpInputAssertionLists;

        /// <summary>
        /// These two regexes are used to select input lists that get dumped
        /// The lists must include *and* must not include the other.
        /// </summary>
        private Regex m_inputAssertionListDumpMustIncludeRegex;
        private Regex m_inputAssertionListDumpMustNotIncludeRegex;

        /// <summary>
        /// If true, dump a content breakdown for each session.
        /// </summary>
        private bool m_runContentBreakdown;

        /// <summary>
        /// This string is used to filter which sessions
        /// get included in the analysis. The session
        /// will only be included if the regex indicates
        /// a match on the name of the session.
        /// </summary>
        private Regex m_sessionRegex;

        /// <summary>
        /// If true, all of the content in the CAS will be
        /// retrieved, rehashed and checked for differences.
        /// </summary>
        /// <remarks>
        /// This is expensive, especially over the network.
        /// </remarks>
        private bool m_rehashCASContent;

        /// <summary>
        /// This is where the results of the tool are
        /// written to. Default is the console. The user
        /// can specify a file to use instead.
        /// </summary>
        private TextWriter m_outputDestination;

        /// <summary>
        /// If an output file is specified, this will contain the base path
        /// of the output file. Used for content breakdown analysis, which
        /// writes multiple output files.
        /// </summary>
        private string m_outputBasePath;

        /// <summary>
        /// Only applies when doing an input assertion list check.
        /// This is the minimum size disparity factor between input
        /// assertion lists corresponding to strong fingerprints
        /// that all have the same weak fingerprint.
        /// </summary>
        /// <remarks>
        /// For example, if one list was 10 bytes and the other list was 22
        /// bytes and this value was 2, this would be considered an anomaly
        /// because 10 * 2 is less than 22. If this value was instead 3, then
        /// it would not be considered an anomaly because 10 * 3 is NOT less
        /// than 22.
        /// </remarks>
        private double m_inputAssertionListSizeDisparityMinimumFactor =
            InputAssertionListChecker.DefaultDisparityFactor;

        /// <summary>
        /// If specified by the user, this is the file that all weak
        /// fingerprints found should be output to. If not specified, this
        /// value will remain null and no weak fingerprints will be output
        /// anywhere.
        /// </summary>
        private string m_weakFingerprintOutputFilepath = null;

        /// <summary>
        /// If the user specifies to output all weak fingerprints found, they
        /// will be put into this collection as the operations run.
        /// </summary>
        private ConcurrentDictionary<WeakFingerprintHash, byte> m_weakFingerprintsFound;

        /// <summary>
        /// If the user specifies a file to pull weak fingerprints from, they
        /// will be stored in this set. If not or if the file is empty, this
        /// set will remain empty.
        /// </summary>
        private ISet<WeakFingerprintHash> m_inputWeakFingerprints = new HashSet<WeakFingerprintHash>();

        /// <summary>
        /// This is the cache being analyzed.
        /// </summary>
        private ICache m_cache;

        /// <summary>
        /// Main method for outputting help text to the screen
        /// </summary>
        private static void WriteHelp()
        {
            HelpWriter writer = new HelpWriter();
            writer.WriteBanner("bxlcacheanalyzer.exe is a tool for doing statistical analysis, input list analysis and consistency checking of a cache.");

            writer.WriteLine("bxlcacheanalyzer.exe [/statisticalAnalysis [/contentAnalysis] | /consistencyCheck [/rehashCASContent] | inputAssertionListDump [/inputAssertionListDumpMustIncludeRegex] [/inputAssertionListDumpMustNotIncludeRegex] | /inputAssertionListCheck [/inputAssertionListSizeDisparityMinimumFactor:double]] | [/contentBreakdown] " +
                                                "[/jsonString:string | /jsonFile:string] " +
                                                "[/sessionIDFilter:string | /weakFingerprintInputFilepath:string] " +
                                                "[/outputFile:string] " +
                                                "[/weakFingerprintOutputFilepath:string]");
            writer.WriteLine();
            writer.WriteOption("statisticalAnalysis", "If specified, performs a statistical analysis of the cache and outputs the results in CSV format", shortName: "sa");
            writer.WriteOption("contentAnalysis", "If specified, includes per-session content size in the statistical analysis", shortName: "ca");
            writer.WriteOption("consistencyCheck", "If specified, performs a consistency check of the cache", shortName: "cc");
            writer.WriteOption("rehashCASContent", "If specified, all content in the CAS will be rehashed to see if any of the content has been corrupted", shortName: "rc");
            writer.WriteOption("inputAssertionListDump", "If specified, all strong fingerprints found will have their input assertion lists dumped", shortName: "id");
            writer.WriteOption("inputAssertionListDumpMustIncludeRegex", "Regex that must match in the input list", shortName: "ii");
            writer.WriteOption("inputAssertionListDumpMustNotIncludeRegex", "Regex that must not match in the input list", shortName: "in");
            writer.WriteOption("inputAssertionListCheck", "If specified, performs a check of all input assertion lists for each weak fingerprint to look for major discrepancies in list size", shortName: "ic");
            writer.WriteOption("inputAssertionListSizeDisparityMinimumFactor", "Sets the minimum factor of size disparity between input assertion lists of same weak fingerprint, defaults to " + InputAssertionListChecker.DefaultDisparityFactor, shortName: "mf");
            writer.WriteOption("contentBreakdown", "If specified, produce a CSV breaking down content for each session.", shortName: "cb");
            writer.WriteOption("jsonString", "JSON Config string of the cache", shortName: "js");
            writer.WriteOption("jsonFile", "File containing the JSON Config string of the cache", shortName: "jf");
            writer.WriteOption("sessionIDFilter", "Limits the analysis to only session IDs that match the specified regex string", shortName: "sf");
            writer.WriteOption("weakFingerprintInputFilepath", "If specified, the requested operations will be done with the weak fingerprints contained in the specified file (except for statisticalAnalysis which is always through sessions)", shortName: "wi");
            writer.WriteOption("outputFile", "The output of the tool is redirected to the file specified. If no file is specified, the output is directed to the console", shortName: "of");
            writer.WriteOption("weakFingerprintOutputFilepath", "If specified, all weak fingerprints found during execution (except for statisticalAnalysis) will be output to the specified file", shortName: "wo");
        }

        /// <summary>
        /// Writes the input string to the screen in red
        /// </summary>
        /// <param name="errorMessage">
        /// The error message to be written
        /// </param>
        private static void WriteError(string errorMessage)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine(errorMessage);
            Console.ForegroundColor = original;
        }

        /// <summary>
        /// Writes the input string to the screen in yellow
        /// </summary>
        /// <param name="warningMessage">
        /// The warning message to be written
        /// </param>
        private static void WriteWarning(string warningMessage)
        {
            ConsoleColor original = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Error.WriteLine(warningMessage);
            Console.ForegroundColor = original;
        }

        /// <summary>
        /// Responsible for parsing the supplied command
        /// line arguments
        /// </summary>
        /// <param name="args">
        /// Array of the command line arguments
        /// </param>
        public Args(string[] args)
            : base(args)
        {
            string jsonFilePath = null;
            string sessionIDFilter = null;
            string inputAssertionListDumpMustIncludeRegex = null;
            string inputAssertionListDumpMustNotIncludeRegex = null;
            string outputFileName = null;
            string weakFingerprintInputFilepath = null;

            foreach (Option opt in Options)
            {
                if (s_helpStrings.Any(s => opt.Name.Equals(s, StringComparison.OrdinalIgnoreCase)))
                {
                    Help = true;
                    WriteHelp();
                    return;
                }
                else if (opt.Name.Equals("statisticalAnalysis", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("sa", StringComparison.OrdinalIgnoreCase))
                {
                    m_runStatisticalAnalysis = true;
                }
                else if (opt.Name.Equals("contentAnalysis", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("ca", StringComparison.OrdinalIgnoreCase))
                {
                    m_analyzeContent = true;
                }
                else if (opt.Name.Equals("consistencyCheck", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("cc", StringComparison.OrdinalIgnoreCase))
                {
                    m_runConsistencyCheck = true;
                }
                else if (opt.Name.Equals("inputAssertionListCheck", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("ic", StringComparison.OrdinalIgnoreCase))
                {
                    m_findInputAssertionListAnomalies = true;
                }
                else if (opt.Name.Equals("contentBreakdown", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("cb", StringComparison.OrdinalIgnoreCase))
                {
                    m_runContentBreakdown = true;
                }
                else if (opt.Name.Equals("jsonString", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("js", StringComparison.OrdinalIgnoreCase))
                {
                    m_jsonString = opt.Value;
                }
                else if (opt.Name.Equals("jsonFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("jf", StringComparison.OrdinalIgnoreCase))
                {
                    jsonFilePath = opt.Value;
                }
                else if (opt.Name.Equals("sessionIDFilter", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("sf", StringComparison.OrdinalIgnoreCase))
                {
                    sessionIDFilter = opt.Value;
                }
                else if (opt.Name.Equals("rehashCASContent", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("rc", StringComparison.OrdinalIgnoreCase))
                {
                    m_rehashCASContent = true;
                }
                else if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("of", StringComparison.OrdinalIgnoreCase))
                {
                    outputFileName = opt.Value;
                }
                else if (opt.Name.Equals("inputAssertionListSizeDisparityMinimumFactor", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("mf", StringComparison.OrdinalIgnoreCase))
                {
                    if (!double.TryParse(opt.Value, out m_inputAssertionListSizeDisparityMinimumFactor))
                    {
                        throw Error("The value for option '" + opt.Name + "' must be a valid double");
                    }

                    if (m_inputAssertionListSizeDisparityMinimumFactor <= 1.0)
                    {
                        WriteWarning("WARNING! The value for option '" + opt.Name + "' must be > 1" +
                            ". Defaulting to " + InputAssertionListChecker.DefaultDisparityFactor);
                        m_inputAssertionListSizeDisparityMinimumFactor = InputAssertionListChecker.DefaultDisparityFactor;
                    }
                }
                else if (opt.Name.Equals("weakFingerprintInputFilepath", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("wi", StringComparison.OrdinalIgnoreCase))
                {
                    weakFingerprintInputFilepath = opt.Value;
                }
                else if (opt.Name.Equals("weakFingerprintOutputFilepath", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("wo", StringComparison.OrdinalIgnoreCase))
                {
                    m_weakFingerprintOutputFilepath = opt.Value;
                }
                else if (opt.Name.Equals("inputAssertionListDump", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("id", StringComparison.OrdinalIgnoreCase))
                {
                    m_dumpInputAssertionLists = true;
                }
                else if (opt.Name.Equals("inputAssertionListDumpMustIncludeRegex", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("ii", StringComparison.OrdinalIgnoreCase))
                {
                    inputAssertionListDumpMustIncludeRegex = opt.Value;
                }
                else if (opt.Name.Equals("inputAssertionListDumpMustNotIncludeRegex", StringComparison.OrdinalIgnoreCase) ||
                   opt.Name.Equals("in", StringComparison.OrdinalIgnoreCase))
                {
                    inputAssertionListDumpMustNotIncludeRegex = opt.Value;
                }
                else
                {
                    WriteWarning("WARNING! Unrecognized command line option: " + opt.Name);
                }
            }

            // Initialize json config string
            bool jsonFileProvided = jsonFilePath != null;

            if (jsonFileProvided)
            {
                m_jsonString = File.ReadAllText(jsonFilePath);
            }

            if (string.IsNullOrEmpty(m_jsonString))
            {
                throw Error("Either the json string must be specified or a path to a file containing the json string must be specified.");
            }

            // Initialize session id regex
            if (string.IsNullOrEmpty(sessionIDFilter))
            {
                m_sessionRegex = new Regex(".*", RegexOptions.Compiled);
            }
            else
            {
                try
                {
                    m_sessionRegex = new Regex(sessionIDFilter, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                }
                catch (Exception e)
                {
                    throw Error("Initializing the session ID regex failed with exception: [{0}].", e);
                }
            }

            if (string.IsNullOrEmpty(inputAssertionListDumpMustIncludeRegex))
            {
                m_inputAssertionListDumpMustIncludeRegex = null;
            }
            else
            {
                try
                {
                    m_inputAssertionListDumpMustIncludeRegex = new Regex(inputAssertionListDumpMustIncludeRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                }
                catch (Exception e)
                {
                    throw Error("Initializing the input assertion list must include regex failed with exception: [{0}].", e);
                }
            }

            if (string.IsNullOrEmpty(inputAssertionListDumpMustNotIncludeRegex))
            {
                m_inputAssertionListDumpMustNotIncludeRegex = null;
            }
            else
            {
                try
                {
                    m_inputAssertionListDumpMustNotIncludeRegex = new Regex(inputAssertionListDumpMustNotIncludeRegex, RegexOptions.Compiled | RegexOptions.CultureInvariant);
                }
                catch (Exception e)
                {
                    throw Error("Initializing the input assertion list must not include regex failed with exception: [{0}].", e);
                }
            }

            // Initialize output destination text writer
            if (string.IsNullOrEmpty(outputFileName))
            {
                m_outputDestination = Console.Out;
                m_outputBasePath = null;
                Console.Error.WriteLine("\nUsing output destination: Console");
            }
            else
            {
                m_outputBasePath = Path.GetDirectoryName(outputFileName);
                FileStream fileStream = null;
                try
                {
                    fileStream = new FileStream(outputFileName, FileMode.Create);
                    m_outputDestination = new StreamWriter(fileStream);
                }
                catch (Exception e)
                {
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                    }

                    throw Error("Opening the output file failed with exception: [{0}]", e);
                }

                Console.Error.WriteLine("\nUsing output destination: " + outputFileName);
            }

            // Read in weak fingerprints to use
            // Expected file format is one weak fingerprint per line, as a hex string
            if (!string.IsNullOrEmpty(weakFingerprintInputFilepath))
            {
                FileStream fileStream = File.OpenRead(weakFingerprintInputFilepath);
                try
                {
                    using (StreamReader streamReader = new StreamReader(fileStream))
                    {
                        fileStream = null;
                        WeakFingerprintHash weakFingerprint;
                        string line;
                        while ((line = streamReader.ReadLine()) != null)
                        {
                            if (WeakFingerprintHash.TryParse(line, out weakFingerprint))
                            {
                                m_inputWeakFingerprints.Add(weakFingerprint);
                            }
                        }
                    }
                }
                finally
                {
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                    }
                }

                if (m_inputWeakFingerprints.Count == 0)
                {
                    throw Error("Could not load any weak fingerprints from {0}", weakFingerprintInputFilepath);
                }
            }

            m_weakFingerprintsFound = string.IsNullOrEmpty(m_weakFingerprintOutputFilepath) ? null : new ConcurrentDictionary<WeakFingerprintHash, byte>();
        }

        private void OutputCacheErrors(IEnumerable<CacheError> cacheErrors)
        {
            Console.Error.WriteLine("\nCache Errors detected: " + cacheErrors.Count());

            bool printedHeader = false;
            foreach (CacheError error in cacheErrors)
            {
                if (!printedHeader)
                {
                    m_outputDestination.WriteLine(CacheError.GetHeader());
                    printedHeader = true;
                }

                m_outputDestination.WriteLine(error.ToString());
            }
        }

        private void OutputWeakFingerprintsToFile()
        {
            if (m_weakFingerprintsFound != null && !string.IsNullOrEmpty(m_weakFingerprintOutputFilepath))
            {
                FileStream fileStream = File.Create(m_weakFingerprintOutputFilepath);
                try
                {
                    using (StreamWriter streamWriter = new StreamWriter(fileStream))
                    {
                        fileStream = null;
                        foreach (WeakFingerprintHash weakFingerprint in m_weakFingerprintsFound.Keys)
                        {
                            streamWriter.WriteLine(weakFingerprint.ToString());
                        }
                    }
                }
                finally
                {
                    if (fileStream != null)
                    {
                        fileStream.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Performs a statistical analysis of the specified cache.
        /// </summary>
        /// <returns>Status code. 0 => success, non-zero => failure</returns>
        public int DoStatisticalAnalysis()
        {
            Console.Error.WriteLine("\nStarting statistical analysis");

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Perform cache analysis
            try
            {
                StatisticalAnalyzer statisticalAnalyzer = new StatisticalAnalyzer(m_cache);
                IEnumerable<SessionChurnInfo> analysisResults = statisticalAnalyzer.Analyze(m_sessionRegex, m_analyzeContent);

                // Write analysis results to file
                m_outputDestination.WriteLine(SessionChurnInfo.GetHeader());
                foreach (SessionChurnInfo sessionChurnInfo in analysisResults)
                {
                    m_outputDestination.WriteLine(sessionChurnInfo);
                }

                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nSessions analyzed/Total sessions: {0}/{1}",
                    statisticalAnalyzer.NumSessionsAnalyzed, statisticalAnalyzer.NumSessions));
            }
            catch (NotImplementedException e)
            {
                // Not all cache implementations implement all of the interface methods
                WriteError("Exception caught: " + e.Message);
                WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a statistical analysis.");
                return 1;
            }

            // Stop timing
            stopwatch.Stop();
            Console.Error.WriteLine("\nTotal time to do statistical analysis: " + stopwatch.Elapsed.TotalSeconds.ToString("F", CultureInfo.CurrentCulture) + " seconds");

            return 0;
        }

        /// <summary>
        /// Performs a consistency check of the specified cache.
        /// If the cache is a VerticalCacheAggregator, a two level
        /// check will be performed. Otherwise, a single level
        /// check will be performed.
        /// </summary>
        /// <returns>Status code. 0 => success, non-zero => failure</returns>
        private int DoConsistencyCheck()
        {
            Console.Error.WriteLine("\nStarting consistency check");

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            IEnumerable<CacheError> cacheErrors;

            VerticalCacheAggregator verticalCacheAggregator = m_cache as VerticalCacheAggregator;
            if (verticalCacheAggregator != null)
            {
                ICache localCache = verticalCacheAggregator.LocalCache;
                ICache remoteCache = verticalCacheAggregator.RemoteCache;
                TwoLevelCacheChecker cacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, m_rehashCASContent);
                if (m_inputWeakFingerprints.Count > 0)
                {
                    Console.Error.WriteLine("\nChecking through the " + m_inputWeakFingerprints.Count + " provided weak fingerprints");
                    cacheErrors = cacheChecker.CheckCache(m_inputWeakFingerprints, m_weakFingerprintsFound).Result;
                }
                else
                {
                    try
                    {
                        Console.Error.WriteLine("\nChecking through the sessions using the following regex: " + m_sessionRegex.ToString());
                        cacheErrors = cacheChecker.CheckCache(m_sessionRegex, m_weakFingerprintsFound).Result;
                        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nSessions checked/Total sessions: {0}/{1}",
                            cacheChecker.NumSessionsChecked, cacheChecker.NumSessions));
                    }
                    catch (NotImplementedException e)
                    {
                        // Not all cache implementations implement all of the interface methods
                        WriteError("Exception caught: " + e.Message);
                        WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a check.");
                        return 1;
                    }
                }

                Console.Error.WriteLine("\nNumber of FullCacheRecords checked: " + cacheChecker.NumFullCacheRecords);
            }
            else
            {
                SingleCacheChecker cacheChecker = new SingleCacheChecker(m_jsonString, m_rehashCASContent);
                if (m_inputWeakFingerprints.Count > 0)
                {
                    Console.Error.WriteLine("\nChecking through the " + m_inputWeakFingerprints.Count + " provided weak fingerprints");
                    cacheErrors = cacheChecker.CheckCache(m_inputWeakFingerprints, m_weakFingerprintsFound).Result;
                }
                else
                {
                    try
                    {
                        Console.Error.WriteLine("\nChecking through the sessions using the following regex: " + m_sessionRegex.ToString());
                        cacheErrors = cacheChecker.CheckCache(m_sessionRegex, m_weakFingerprintsFound).Result;
                        Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nSessions checked/Total sessions: {0}/{1}",
                            cacheChecker.NumSessionsChecked, cacheChecker.NumSessions));
                    }
                    catch (NotImplementedException e)
                    {
                        // Not all cache implementations implement all of the interface methods
                        WriteError("Exception caught: " + e.Message);
                        WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a check.");
                        return 1;
                    }
                }

                Console.Error.WriteLine("\nNumber of FullCacheRecords checked: " + cacheChecker.AllFullCacheRecords.Count);
            }

            // Output cache errors found during check
            OutputCacheErrors(cacheErrors);

            // Stop timing
            stopwatch.Stop();
            Console.Error.WriteLine("\nTotal time to check cache: " + stopwatch.Elapsed.TotalSeconds.ToString("F", CultureInfo.CurrentCulture) + " seconds");

            return 0;
        }

        /// <summary>
        /// Checks for wide variations in the size of input assertion list file sizes corresponding to the same weak fingerprint
        /// </summary>
        /// <returns>Status code. 0 => success, non-zero => failure</returns>
        private int CheckForInputListAnomalies()
        {
            Console.Error.WriteLine("\nStarting check for input assertion list anomalies");

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            InputAssertionListChecker inputAssertionListChecker = new InputAssertionListChecker(m_cache, m_inputAssertionListSizeDisparityMinimumFactor);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();

            IEnumerable<InputAssertionListAnomaly> inputAssertionListAnomalies;

            if (m_inputWeakFingerprints.Count > 0)
            {
                // Check with user provided weak fingerprints
                Console.Error.WriteLine("\nChecking through the " + m_inputWeakFingerprints.Count + " provided weak fingerprints");
                inputAssertionListAnomalies = inputAssertionListChecker.PerformAnomalyCheck(m_inputWeakFingerprints, cacheErrors, m_weakFingerprintsFound);
            }
            else
            {
                // Check through sessions
                try
                {
                    Console.Error.WriteLine("\nChecking through the sessions using the following regex: " + m_sessionRegex.ToString());
                    inputAssertionListAnomalies = inputAssertionListChecker.PerformAnomalyCheck(m_sessionRegex, cacheErrors, m_weakFingerprintsFound);
                }
                catch (NotImplementedException e)
                {
                    // Not all cache implementations implement all of the interface methods
                    WriteError("Exception caught: " + e.Message);
                    WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a check.");
                    return 1;
                }
            }

            // Output input assertion list anomalies
            int numberOfInputAssertionListAnomalies = 0;
            foreach (InputAssertionListAnomaly inputAssertionListAnomaly in inputAssertionListAnomalies)
            {
                m_outputDestination.WriteLine(inputAssertionListAnomaly.ToString());
                numberOfInputAssertionListAnomalies++;
            }

            if (m_inputWeakFingerprints.Count == 0)
            {
                Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nSessions checked/Total sessions: {0}/{1}",
                    inputAssertionListChecker.NumSessionsChecked, inputAssertionListChecker.NumSessions));
            }

            Console.Error.WriteLine(string.Format(CultureInfo.InvariantCulture, "\nWeak fingerprints with 2+ strong fingerprints/Total weak fingerprints: {0}/{1}",
                    inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs, inputAssertionListChecker.NumWeakFingerprintsChecked));
            Console.Error.WriteLine("\nInput Assertion List Anomalies found: " + numberOfInputAssertionListAnomalies);

            // Output any cache errors found during check
            OutputCacheErrors(cacheErrors.Keys);

            // Stop timing
            stopwatch.Stop();
            Console.Error.WriteLine("\nTotal time to check for input assertion list anomalies: " + stopwatch.Elapsed.TotalSeconds.ToString("F", CultureInfo.CurrentCulture) + " seconds");

            return 0;
        }

        /// <summary>
        /// Dumps the contents of every input assertion list that it can
        /// </summary>
        /// <returns>Status code. 0 => success, non-zero => failure</returns>
        private int DumpInputAssertionLists()
        {
            Console.Error.WriteLine("\nStarting dump of input assertion lists");

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            InputAssertionListChecker inputAssertionListChecker = new InputAssertionListChecker(m_cache);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();

            IEnumerable<InputAssertionList> inputAssertionLists;

            Func<string, bool> inputListCheck;
            if (m_inputAssertionListDumpMustIncludeRegex == null)
            {
                if (m_inputAssertionListDumpMustNotIncludeRegex == null)
                {
                    inputListCheck = (inputList) => true;
                }
                else
                {
                    inputListCheck = (inputList) => !m_inputAssertionListDumpMustNotIncludeRegex.IsMatch(inputList);
                }
            }
            else
            {
                if (m_inputAssertionListDumpMustNotIncludeRegex == null)
                {
                    inputListCheck = (inputList) => m_inputAssertionListDumpMustIncludeRegex.IsMatch(inputList);
                }
                else
                {
                    inputListCheck = (inputList) => m_inputAssertionListDumpMustIncludeRegex.IsMatch(inputList) && !m_inputAssertionListDumpMustNotIncludeRegex.IsMatch(inputList);
                }
            }

            if (m_inputWeakFingerprints.Count > 0)
            {
                // Get lists with user provided weak fingerprints
                Console.Error.WriteLine("\nChecking through the " + m_inputWeakFingerprints.Count + " provided weak fingerprints");
                inputAssertionLists = inputAssertionListChecker.GetSuspectInputAssertionLists(m_inputWeakFingerprints, inputListCheck, cacheErrors, m_weakFingerprintsFound);
            }
            else
            {
                // Get lists through sessions
                try
                {
                    Console.Error.WriteLine("\nChecking through the sessions using the following regex: " + m_sessionRegex.ToString());
                    inputAssertionLists = inputAssertionListChecker.GetSuspectInputAssertionLists(m_sessionRegex, inputListCheck, cacheErrors, m_weakFingerprintsFound);
                }
                catch (NotImplementedException e)
                {
                    // Not all cache implementations implement all of the interface methods
                    WriteError("Exception caught: " + e.Message);
                    WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a check.");
                    return 1;
                }
            }

            // Output input assertion lists
            List<StrongFingerprint> fingerprints = new List<StrongFingerprint>();
            foreach (var inputAssertion in inputAssertionLists)
            {
                fingerprints.Add(inputAssertion.StrongFingerprintValue);
                m_outputDestination.WriteLine(string.Format(CultureInfo.InvariantCulture, "StrongFingerprint: {0}\n{1}\n",
                                                            inputAssertion.StrongFingerprintValue,
                                                            inputAssertion.InputAssertionListContents));
            }

            // It is not until here where the summary information is complete.
            string summary;
            if (inputAssertionListChecker.NumSessionsChecked > 0)
            {
                summary = string.Format(CultureInfo.InvariantCulture, "\nFound {0} matching fingerprints out of {1} unique input lists from {2} sessions\n",
                                            fingerprints.Count,
                                            inputAssertionListChecker.NumInputListsChecked,
                                            inputAssertionListChecker.NumSessionsChecked);
            }
            else
            {
                summary = string.Format(CultureInfo.InvariantCulture, "\nFound {0} matching fingerprints out of {1} unique input lists\n",
                                        fingerprints.Count,
                                        inputAssertionListChecker.NumInputListsChecked);
            }

            m_outputDestination.WriteLine(summary);

            if (inputAssertionListChecker.NumSessionsChecked > 0)
            {
                foreach (var fingerprint in fingerprints)
                {
                    var sessionIds = inputAssertionListChecker.GetSessionsWithFingerprint(fingerprint).ToList();

                    if (sessionIds.Count > 0)
                    {
                        m_outputDestination.WriteLine("{0} sessions contain the strong fingerprint {1}:", sessionIds.Count, fingerprint);
                        foreach (var sessionId in sessionIds)
                        {
                            m_outputDestination.WriteLine("  Session: {0}", sessionId);
                        }

                        m_outputDestination.WriteLine();
                    }
                }
            }

            // Output any cache errors found during input list dump
            OutputCacheErrors(cacheErrors.Keys);

            // Stop timing
            stopwatch.Stop();

            Console.Error.WriteLine(summary);
            Console.Error.WriteLine("Total time to dump input assertion lists: " + stopwatch.Elapsed.TotalSeconds.ToString("F", CultureInfo.InvariantCulture) + " seconds");

            return 0;
        }

        private int DoContentBreakdown()
        {
            var percentiles = new int[] { 10, 20, 30, 40, 50, 60, 70, 80, 90 };

            Console.Error.WriteLine("\nStarting content breakdown.\n");

            // Start timing
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();

            // Perform cache analysis
            try
            {
                var analyzer = new ContentBreakdownAnalyzer(m_cache);
                var results = analyzer.Analyze(m_sessionRegex);

                foreach (var result in results)
                {
                    m_outputDestination.WriteLine("Session: {0}\n", result.SessionName);
                    m_outputDestination.WriteLine(
                        "  {0} CasElements totaling {1} bytes",
                        result.CasElementSizes.Count,
                        result.CasElementSizes.Sizes.Sum());

                    m_outputDestination.WriteLine("  CasElement size breakdown:");
                    m_outputDestination.WriteLine("    %ile     Content Size      Cumulative");
                    var p = result.CasElementSizes.Sizes.GetPercentilesAndCdg(percentiles);
                    foreach (KeyValuePair<int, Tuple<long, long>> kvp in p)
                    {
                        m_outputDestination.WriteLine("    P{0,3} : {1,15:n0} {2,15:n0}", kvp.Key, kvp.Value.Item1, kvp.Value.Item2);
                    }

                    m_outputDestination.WriteLine();
                    m_outputDestination.WriteLine(
                        "  {0} CasEntries totaling {1} bytes",
                        result.CasEntrySizes.Count,
                        result.CasEntrySizes.Sizes.Sum());

                    m_outputDestination.WriteLine("  CasEntry size breakdown:");
                    m_outputDestination.WriteLine("    %ile     Content Size      Cumulative");
                    p = result.CasEntrySizes.Sizes.GetPercentilesAndCdg(percentiles);
                    foreach (KeyValuePair<int, Tuple<long, long>> kvp in p)
                    {
                        m_outputDestination.WriteLine("    P{0,3} : {1,15:n0} {2,15:n0}", kvp.Key, kvp.Value.Item1, kvp.Value.Item2);
                    }

                    m_outputDestination.WriteLine();

                    // If requested, generate a CSV of content for this result
                    if (m_outputBasePath != null)
                    {
                        string sessionOutputFilename = Path.Combine(m_outputBasePath, result.SessionName + ".CasElements");
                        result.CasElementSizes.WriteCSV(sessionOutputFilename);

                        sessionOutputFilename = Path.Combine(m_outputBasePath, result.SessionName + ".CasEntries");
                        result.CasEntrySizes.WriteCSV(sessionOutputFilename);
                    }
                }
            }
            catch (NotImplementedException e)
            {
                // Not all cache implementations implement all of the interface methods
                WriteError("Exception caught: " + e.Message);
                WriteError("The implementation of the specified cache does not implement all required methods for this tool to be able to perform a statistical analysis.");
                return 1;
            }

            // Stop timing
            stopwatch.Stop();
            Console.Error.WriteLine("\nTotal time to do content breakdown: " + stopwatch.Elapsed.TotalSeconds.ToString("F", CultureInfo.CurrentCulture) + " seconds");

            return 0;
        }

        /// <summary>
        /// Runs the cache analyzer. Performs a statistical analysis and/or a
        /// consistency check and/or an input assertion list check of the cache.
        /// </summary>
        /// <returns>Status code. 0 => success, non-zero => failure</returns>
        internal int RunAnalyzer()
        {
            if (!(m_runStatisticalAnalysis || m_runConsistencyCheck || m_findInputAssertionListAnomalies || m_dumpInputAssertionLists || m_runContentBreakdown))
            {
                WriteError("You must specify to do a statistical analysis (/sa) and/or a consistency check (/cc) and/or an input assertion list check (/ic) and/or an input assertion list dump (/id) and/or a content breakdown (/cb).");
                return 1;
            }

            Console.Error.WriteLine("\nUsing the following json string: " + m_jsonString);

            Possible<ICache, Failure> possibleCache = CacheFactory.InitializeCacheAsync(m_jsonString, default(Guid)).Result;
            if (!possibleCache.Succeeded)
            {
                WriteError("Cache initialization failed: " + possibleCache.Failure.Describe());
                return 1;
            }

            m_cache = possibleCache.Result;

            int returnValue = 1;

            if (m_runStatisticalAnalysis)
            {
                returnValue = DoStatisticalAnalysis();
                if (returnValue != 0)
                {
                    return returnValue;
                }
            }

            if (m_runConsistencyCheck)
            {
                returnValue = DoConsistencyCheck();
                if (returnValue != 0)
                {
                    return returnValue;
                }
            }

            if (m_findInputAssertionListAnomalies)
            {
                returnValue = CheckForInputListAnomalies();
                if (returnValue != 0)
                {
                    return returnValue;
                }
            }

            if (m_dumpInputAssertionLists)
            {
                returnValue = DumpInputAssertionLists();
                if (returnValue != 0)
                {
                    return returnValue;
                }
            }

            if (m_runContentBreakdown)
            {
                returnValue = DoContentBreakdown();
                if (returnValue != 0)
                {
                    return returnValue;
                }
            }

            OutputWeakFingerprintsToFile();

            return returnValue;
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        internal void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
                if (disposing)
                {
                    if (m_outputDestination != null)
                    {
                        m_outputDestination.Dispose();
                    }

                    if (m_cache != null)
                    {
                        m_cache.ShutdownAsync().Wait();
                    }
                }
            }
        }

        ~Args()
        {
           Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
