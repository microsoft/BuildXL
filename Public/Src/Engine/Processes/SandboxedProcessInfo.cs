// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Native.Processes;
using BuildXL.Processes.Containers;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.BuildParameters;

namespace BuildXL.Processes
{
    /// <summary>
    /// Data-structure that holds all information needed to launch a sandboxed process.
    /// </summary>
    public sealed class SandboxedProcessInfo
    {
        private const int BufSize = 4096;

        /// <summary>
        /// Windows-imposed limit on command-line length
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms682425(v=vs.85).aspx
        /// The maximum length of this string is 32,768 characters, including the Unicode terminating null character.
        /// </remarks>
        public const int MaxCommandLineLength = short.MaxValue;

        /// <summary>
        /// Make sure we always wait for a moment after the main process exits by default.
        /// </summary>
        /// <remarks>
        /// We observed that e.g. cmd.exe spawns conhost.exe which tends to outlive cmd.exe for a brief moment (under ideal conditions).
        /// Across OS versions and under heavy load, experience shows one needs to be fairly generous.
        /// </remarks>
        public static readonly TimeSpan DefaultNestedProcessTerminationTimeout = TimeSpan.FromSeconds(30);

        private string m_arguments;

        private string m_commandLine;

        private byte[] m_environmentBlock;

        private string m_rootMappingBlock;

        private int m_maxLengthInMemory = 16384;

        /// <summary>
        /// The logging context used to log the messages to.
        /// </summary>
        public LoggingContext LoggingContext { get; private set; }

        /// <summary>
        /// A detours event listener.
        /// </summary>
        public IDetoursEventListener DetoursEventListener { get; private set; }

        /// <summary>
        /// A macOS kernel extension connection.
        /// </summary>
        public IKextConnection SandboxedKextConnection;

        /// <summary>
        /// Holds the path remapping information for a process that needs to run in a container
        /// </summary>
        public ContainerConfiguration ContainerConfiguration { get; }

        /// <summary>
        /// Creates instance
        /// </summary>
        public SandboxedProcessInfo(
            ISandboxedProcessFileStorage fileStorage,
            string fileName,
            bool disableConHostSharing,
            bool testRetries = false,
            LoggingContext loggingContext = null,
            IDetoursEventListener detourseEventListener = null,
            IKextConnection sandboxedKextConnection = null)
            : this(new PathTable(), fileStorage, fileName, disableConHostSharing, testRetries, loggingContext, detourseEventListener, sandboxedKextConnection)
        {
        }

        /// <summary>
        /// Creates instance
        /// </summary>
        public SandboxedProcessInfo(
            PathTable pathTable,
            ISandboxedProcessFileStorage fileStorage,
            string fileName,
            FileAccessManifest fileAccessManifest,
            bool disableConHostSharing,
            ContainerConfiguration containerConfiguration,
            bool testRetries = false,
            LoggingContext loggingContext = null,
            IDetoursEventListener detoursEventListener = null,
            IKextConnection sandboxedKextConnection = null)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(fileStorage != null);
            Contract.Requires(fileName != null);

            PathTable = pathTable;
            FileAccessManifest = fileAccessManifest;
            FileStorage = fileStorage;
            FileName = fileName;
            DisableConHostSharing = disableConHostSharing;

            // This should be set for testing purposes only.
            TestRetries = testRetries;

            NestedProcessTerminationTimeout = DefaultNestedProcessTerminationTimeout;
            LoggingContext = loggingContext;
            DetoursEventListener = detoursEventListener;
            SandboxedKextConnection = sandboxedKextConnection;
            ContainerConfiguration = containerConfiguration;
        }

        /// <summary>
        /// Creates instance for test
        /// </summary>
        public SandboxedProcessInfo(
            PathTable pathTable,
            ISandboxedProcessFileStorage fileStorage,
            string fileName,
            bool disableConHostSharing,
            bool testRetries = false,
            LoggingContext loggingContext = null,
            IDetoursEventListener detoursEventListener = null,
            IKextConnection sandboxedKextConnection = null,
            ContainerConfiguration containerConfiguration = null,
            FileAccessManifest fileAccessManifest = null)
            : this(
                  pathTable,
                  fileStorage,
                  fileName,
                  fileAccessManifest ?? new FileAccessManifest(pathTable),
                  disableConHostSharing,
                  containerConfiguration ?? ContainerConfiguration.DisabledIsolation,
                  testRetries,
                  loggingContext,
                  detoursEventListener,
                  sandboxedKextConnection)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(fileStorage != null);
            Contract.Requires(fileName != null);
        }

        /// <summary>
        /// A special flag used for testing purposes only.
        /// If true it executes the retry process execution logic.
        /// </summary>
        public bool TestRetries { get; }

        /// <summary>
        /// The path table.
        /// </summary>
        public PathTable PathTable { get; }

        /// <summary>
        /// White-list of allowed file accesses, and general file access reporting flags
        /// </summary>
        public FileAccessManifest FileAccessManifest { get; }

        /// <summary>
        /// Access to file storage
        /// </summary>
        public ISandboxedProcessFileStorage FileStorage { get; }

        /// <summary>
        /// Access to disableConHostSharing
        /// </summary>
        public bool DisableConHostSharing { get; }

        /// <summary>
        /// Name of executable (pure path name; no command-line encoding)
        /// </summary>
        public string FileName { get; }

        /// <summary>
        /// How to decode the standard input; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardInputEncoding { get; set; }

        /// <summary>
        /// How to decode the standard output; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardErrorEncoding { get; set; }

        /// <summary>
        /// How to decode the standard output; if not set, encoding of current process is used
        /// </summary>
        public Encoding StandardOutputEncoding { get; set; }

        /// <summary>
        /// Encoded command line arguments
        /// </summary>
        public string Arguments
        {
            get
            {
                return m_arguments;
            }

            set
            {
                m_arguments = value;
                m_commandLine = null;
            }
        }

        /// <summary>
        /// Working directory (can be null)
        /// </summary>
        public string WorkingDirectory { get; set; }

        /// <summary>
        /// Environment variables (can be null)
        /// </summary>
        public IBuildParameters EnvironmentVariables { get; set; }

        /// <summary>
        /// Environment variables (can be null)
        /// </summary>
        public IReadOnlyDictionary<string, string> RootMappings { get; set; }

        /// <summary>
        /// Optional standard input stream from which to read
        /// </summary>
        public TextReader StandardInputReader { get; set; }

        /// <summary>
        /// Optional observer of each output line
        /// </summary>
        public Action<string> StandardOutputObserver { get; set; }

        /// <summary>
        /// Optional observer of each output line
        /// </summary>
        public Action<string> StandardErrorObserver { get; set; }

        /// <summary>
        /// Allowed surviving child processes.
        /// </summary>
        public string[] AllowedSurvivingChildProcessNames { get; set; }

        /// <summary>
        /// Max. number of characters buffered in memory before output is streamed to disk
        /// </summary>
        public int MaxLengthInMemory
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return m_maxLengthInMemory;
            }

            set
            {
                Contract.Requires(value >= 0);
                m_maxLengthInMemory = value;
            }
        }

        /// <summary>
        /// Number of bytes for output buffers
        /// </summary>
        public static int BufferSize => BufSize;

        /// <summary>
        /// Gets the command line, comprised of the executable file name and the arguments.
        /// </summary>
        [Pure]
        public string GetCommandLine()
        {
            m_commandLine = m_commandLine ?? CommandLineEscaping.EscapeAsCreateProcessApplicationName(FileName) + " " + Arguments;
            return m_commandLine;
        }

        /// <summary>
        /// Gets the root mappings as a unicode block.
        /// Format: ((drive letter unicode character)(null terminated target path string))* (null character)
        /// </summary>
        [Pure]
        public string GetUnicodeRootMappingBlock()
        {
            IReadOnlyDictionary<string, string> rootMappings = RootMappings;
            if (rootMappings == null)
            {
                return string.Empty;
            }

            if (m_rootMappingBlock == null)
            {
                using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
                {
                    StringBuilder stringBuff = wrap.Instance;
                    foreach (var rootMapping in rootMappings)
                    {
                        stringBuff.Append(rootMapping.Key[0]);
                        stringBuff.Append(rootMapping.Value);
                        stringBuff.Append('\0');
                    }

                    // an extra null at the end indicates end of list.
                    stringBuff.Append('\0');

                    m_rootMappingBlock = stringBuff.ToString();
                }
            }

            return m_rootMappingBlock;
        }

        /// <summary>
        /// Gets the current environment variables, if any, as a unicode environment block
        /// </summary>
        [Pure]
        public byte[] GetUnicodeEnvironmentBlock()
        {
            return m_environmentBlock ?? (m_environmentBlock = ProcessUtilities.SerializeEnvironmentBlock(EnvironmentVariables?.ToDictionary()));
        }

        /// <summary>
        /// Optional total wall clock time limit on process
        /// </summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>
        /// Wall clock time limit to wait for nested processes to exit after main process has terminated
        /// </summary>
        /// <remarks>
        /// After the time is up, we kill the corresponding job object and complain about surviving nested processes.
        /// </remarks>
        public TimeSpan NestedProcessTerminationTimeout { get; set; }

        /// <summary>
        /// Pip's semi stable hash. Used for logging.
        /// </summary>
        public long PipSemiStableHash { get; set; }

        /// <summary>
        /// Root directory where timeout dumps for the process should be stored. This directory may contain other outputs
        /// for the process.
        /// </summary>
        public string TimeoutDumpDirectory { get; set; }

        /// <summary>
        /// The kind of sandboxing to use.
        /// </summary>
        public SandboxKind SandboxKind { get; set; }

        /// <summary>
        /// Pip's Description. Used for logging.
        /// </summary>
        public string PipDescription { get; set; }

        /// <summary>
        /// Notify this delegate once process id becomes available.
        /// </summary>
        public Action<int> ProcessIdListener { get; set; }
    }
}
