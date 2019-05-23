// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime;
using System.Security.Principal;
using System.Text;
using System.Threading;
using BuildXL.App.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Strings = bxl.Strings;

namespace BuildXL
{
    /// <summary>
    /// Server wrapper for running one or more <see cref="BuildXLApp" /> instances.
    /// An 'app server' is a somewhat persistent process which (one or more times) runs an 'app' given a command line,
    /// and streams to a client the resulting console output and exit code. Running an 'app server' is just like running
    /// each app in its own shorter-lived process, but has the advantage that we can amortize startup overhead (JIT,
    /// static initialization, and loading of files such as build graphs).
    /// </summary>
    internal sealed class AppServer : IAppHost, IDisposable
    {
        /// <summary>
        /// <see cref="NamedPipeClientStream" /> loops on <c>ERROR_FILE_NOT_FOUND</c> for some reason (i.e., no pipe listening)
        /// in addition to waiting for busy instances (i.e., <c>WaitNamedPipe</c>)
        /// We don't want either behavior.
        /// </summary>
        private const int ConnectTimeoutInMilliseconds = 0;

        /// <summary>
        /// Time to wait for a newly launched server process to start up and signal its event.
        /// </summary>
        private const int StartupTimeoutInMilliseconds = 5000;

        private const int BufferSize = 8192;

        // Message prefixes for the app-server protocol.
        private const byte ServerHelloMessage = 0xD0;
        private const byte ConsoleOutputMessage = 0xC0;
        private const byte ConsoleProgressMessage = 0xC1;
        private const byte ConsoleTemporaryMessage = 0xC2;
        private const byte ConsoleTemporaryOnlyMessage = 0xC3;
        private const byte ExitCodeMessage = 0xEC;
        private const byte CancelMessage = 0xCD;
        private const byte TerminateMessage = 0xCE;

        private readonly TimeSpan m_maximumIdleTime;
        private readonly Stopwatch m_idleTime = new Stopwatch();
        private PerformanceSnapshot m_startSnapshot;
        private int m_timesUsed;
        private EngineState m_engineState;
        private StartupParameters m_startupParameters;

        /// <summary>
        /// Timestamp based hash which represents the server deployment directory where the server is running.
        /// </summary>
        public string TimestampBasedHash => m_startupParameters?.TimestampBasedHash;

        /// <summary>
        /// Writer to the client process pipe. This is held as an instance method only for the sake of letting the
        /// unhandled exception handler communicate the crash's exit code to the client process.
        /// </summary>
        private BinaryWriter m_writer;
        private readonly object m_writerLock = new object();

        /// <summary>
        /// Starting patterns for environment variables which when modified will cause a new instance of the server
        /// process to start.
        /// </summary>
        /// <remarks>
        /// COMPLUS: Any variable starting with COMPLUS may impact how the runtime is loaded. Changes to these may impact
        /// how the CLR initializes in the currently running process.
        /// DEVPATH: Impacts the runtime search for loading assemblies. It might impact how the already running server
        /// process would have loaded assemblies.
        /// </remarks>
        private static readonly string[] s_poisonVariablePatterns = new string[] { "COMPLUS", "DEVPATH" };

        #region Server: Accepting client connections and running apps

        /// <nodoc />
        public AppServer(TimeSpan maximumIdleTime)
        {
            Contract.Requires(maximumIdleTime != TimeSpan.Zero);
            m_maximumIdleTime = maximumIdleTime;
        }

        /// <summary>
        /// Runs an app server (establishes a listener for connecting clients, and runs one or more apps as they connect).
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods",
            Justification = "Knowingly calling GC.Collect to minimize memory usage of server process while in background.")]
        [SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times",
            Justification = "A using statement is used for both the BufferedStream and NamedPipeServerStream.")]
        public ExitKind Run(StartupParameters startupParameters)
        {
            Contract.Requires(startupParameters != null);

            m_startupParameters = startupParameters;
            string pipeName = GetPipeName(startupParameters.UniqueAppServerName);

            using (NamedPipeServerStream pipeInstance = CreateServerPipe(pipeName, startupParameters, out bool pipeCreateSuccess))
            {
                if (!pipeCreateSuccess)
                {
                    return ExitKind.InternalError;
                }

                Contract.Assert(pipeInstance != null);

                using (BufferedStream bufferedStream = new BufferedStream(pipeInstance))
                {
                    while (true)
                    {
                        m_idleTime.Restart();
                        if (!TryWaitForConnection(pipeInstance, pipeName, m_maximumIdleTime))
                        {
                            // Idle time reached; exiting.
                            return ExitKind.BuildSucceeded;
                        }

                        if (!RunAppInstance(bufferedStream))
                        {
                            // Server needs to die because there is a potential mismatch in the serverdeployment directory mismatch.
                            return ExitKind.InfrastructureError;
                        }
                        bufferedStream.Flush();

                        // RunAppInstance presumably has written some response. We have to call FlushFileBuffers via this wrapper
                        // or otherwise DisconnectNamedPipe (below) will delete the shared buffer, i.e., the response would racily
                        // be truncated.
                        pipeInstance.WaitForPipeDrain();

                        // Disconnecting is required to make this instance usable again - even if the client has gone away already.
                        pipeInstance.Disconnect();

                        // Perform a final GC to minimize the memory footprint of the server process. This should only
                        // be done after the client is disconnected otherwise it would slow down the actual build
                        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                        GC.Collect();
                    }
                }
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "Caller is responsible for disposing")]
        private static NamedPipeServerStream CreateServerPipe(string pipeName, StartupParameters startupParameters, out bool success)
        {
            try
            {
                success = true;
                return new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    transmissionMode: PipeTransmissionMode.Byte,

                    // Asynchronous mode appears to be required to keep the cancel-message read
                    // from this pipe below from blocking the log-message write to it later.
                    options: PipeOptions.Asynchronous,
                    inBufferSize: BufferSize,
                    outBufferSize: BufferSize);
            }
            catch (IOException)
            {
                // Signal that startup failed so the client doesn't bother trying to connect. It is important that this
                // takes place before signaling that startup completed
                startupParameters.SignalStartupFailed();

                success = false;
                return null;
            }
            finally
            {
                // Clients can now try connecting. The pipe server may or may not have initialized correctly but we
                // signal either way so clients don't wait the entire timeout on failures.
                startupParameters.SignalStartupAttemptComplete();
            }
        }

        /// <summary>
        /// Single invocation of a <see cref="BuildXLApp" /> by an app server.
        /// Corresponds to the client-side <see cref="Connection.RunWithArgs" />.
        /// </summary>
        /// <returns>
        /// True when the server will continue to respond connection requests.
        /// </returns>
        [SuppressMessage("Microsoft.Usage", "CA2202:DoNotDisposeObjectsMultipleTimes")]
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        private bool RunAppInstance(BufferedStream bufferedStream)
        {
            // Server need to die in certain error types.
            bool killServer = false;

            // Save off the current directory before performing the build. We want to set the server process'
            // current directory back to this directory while it is dormant in the background so it doesn't hold a
            // lock on the build's current directory after it is completed
            string initialDirectory = Environment.CurrentDirectory;

            var pathTable = new PathTable();

            using (var writer = new BinaryWriter(bufferedStream, Encoding.UTF8, leaveOpen: true))
            using (var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true))
            {
                m_writer = writer;

                // Reads data from client.
                BuildXLAppServerData serverData;

                try
                {
                    // Write 'hello' to tell the client we don't intend to disconnect.
                    // This is read and expected in TryConnect.
                    writer.Write(ServerHelloMessage);
                    writer.Flush();
                    serverData = BuildXLAppServerData.Deserialize(reader);
                }
                catch (Exception ex) when (ex is IOException || ex is EndOfStreamException)
                {
                    // Client may have terminated abruptly.
                    return killServer;
                }

                // Set the environment variables and directory directory before parsing the command line arguments since
                // the current directory may be needed for parsing relative paths specified on the command line
                SetEnvironmentVariables(serverData.EnvironmentVariables);
                Directory.SetCurrentDirectory(serverData.CurrentDirectory);

                var console = new AppServerForwardingConsole(writer);

                if (!Args.TryParseArguments(serverData.RawArgs.ToArray(), pathTable, console, out ICommandLineConfiguration configuration))
                {
                    configuration = null;
                }

                ExitKind exit;
                if (configuration != null)
                {
                    // Raw args do not have path to bxl.exe, adding it manually.
                    var rawArgsWithEnginePath = new List<string>(serverData.RawArgs.Count + 1) { serverData.ClientPath };
                    rawArgsWithEnginePath.AddRange(serverData.RawArgs);

                    // Note that we override startTimeUtc to not be the start time of this process (since we reuse it for long periods).
                    using (var app = new BuildXLApp(
                        this,
                        console,
                        configuration,
                        startTimeUtc: serverData.ClientStartTime,
                        commandLineArguments: rawArgsWithEnginePath,
                        pathTable: pathTable,
                        serverModeStatusAndPerf: serverData.ServerModeStatusAndPerf))
                    {
                        // Create a thread to listen for cancel.
                        var listeningClientThread = new Thread(() => ListenClient(app, console, reader, writer))
                        {
                            Name = "Cancellation Event Waiter",
                        };
                        listeningClientThread.Start();

                        var result = app.Run(m_engineState);

                        exit = result.ExitKind;
                        m_engineState = result.EngineState;
                        killServer = result.KillServer;
                    }

                    // Normally each table will get unique static debugging state. But for a long lived server process
                    // the finite slots used for that state can quickly fill up. The deserialized state from this build
                    // may also clash with that of the next build. Therefore we must reset this static state after each
                    // iteration of the engine. We don't worry about doing so on an ungraceful exit since the server process
                    // won't be around anymore by that time anyway.
                    HierarchicalNameTable.ResetStaticDebugState();
                }
                else
                {
                    exit = ExitKind.InvalidCommandLine;
                }

                // Reset the current directory to what it was initially set to. This must be done before
                // allowing the client to exit in case there's an externally configured mount (subst) that is only
                // valid for the duration of the build.
                Directory.SetCurrentDirectory(initialDirectory);

                WriteExitCodeToClient(exit);
                lock (m_writerLock)
                {
                    m_writer = null;
                }
            }

            return !killServer;
        }

        /// <summary>
        /// Writes the exit code to the client. This is only intended to be called outside this class by the app domain
        /// unhandled exception handler.
        /// </summary>
        internal void WriteExitCodeToClient(ExitKind exit)
        {
            lock (m_writerLock)
            {
                if (m_writer != null)
                {
                    m_writer.Write(ExitCodeMessage);
                    m_writer.Write((byte)exit);
                    m_writer.Flush();
                }
            }
        }

        private static void ListenClient(BuildXLApp app, AppServerForwardingConsole console, BinaryReader reader, BinaryWriter writer)
        {
            try
            {
                while (true)
                {
                    int read = reader.ReadByte();

                    bool isTermination = false;
                    if (read == TerminateMessage)
                    {
                        isTermination = true;
                    }
                    else if (read != CancelMessage)
                    {
                        // We're seeing unnecessary crashes due to this and are not sure why. Stop crashing but still
                        // record that it's happening. Then we just treat it like a ctrl-c
                        app.OnUnexpectedCondition("Unknown message received from app client: " + read);
                    }

                    Volatile.Write(ref console.CancellationRequested, true);

                    if (app.OnConsoleCancelEvent(isTermination))
                    {
                        // Don't send anything to the client since the client may have terminated abruptly.
                        Environment.Exit(ExitCode.FromExitKind(ExitKind.Aborted));
                    }
                }
            }
            catch (Exception ex) when (
                ex is ObjectDisposedException 
                || ex is EndOfStreamException
                || ex is IOException /* Client dies before ReadByte completes ReadByte. */)
            {
                // Reading after the app finished.
                return;
            }
        }

        internal static void SetEnvironmentVariables(IEnumerable<KeyValuePair<string, string>> environmentVariables)
        {
            // Only preserve environment variables that don't support being passed through from the client process.
            // All others get reset.
            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                var variableName = entry.Key.ToString();
                if (IsEnvironmentVariablePassThrough(variableName))
                {
                    Environment.SetEnvironmentVariable(variableName, null);
                }
            }

            // Set the variables passed through from the client process
            foreach (KeyValuePair<string, string> variable in environmentVariables)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }

            // Now that environment variables are set, reset engine environment settings to pick up new env vars
            EngineEnvironmentSettings.Reset();
        }

        /// <summary>
        /// Checks if an environment variable is one that supports being passed through and modified within the already
        /// running server process
        /// </summary>
        internal static bool IsEnvironmentVariablePassThrough(string variableName)
        {
            foreach (string poisonPattern in s_poisonVariablePatterns)
            {
                if (variableName.StartsWith(poisonPattern, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class AppServerForwardingConsole : IConsole
        {
            internal bool CancellationRequested;

            private readonly object m_lock = new object();
            private readonly BinaryWriter m_writer;
            private bool m_disabled;

            internal AppServerForwardingConsole(BinaryWriter writer)
            {
                m_writer = writer;
            }

            public void Dispose()
            {
                Volatile.Write(ref m_disabled, true);
            }

            public void WriteOutputLine(MessageLevel messageLevel, string line)
            {
                WriteMessage(() =>
                {
                    m_writer.Write(ConsoleOutputMessage);
                    m_writer.Write((byte)messageLevel);
                    m_writer.Write(line);
                });
            }

            /// <inheritdoc />
            public void ReportProgress(ulong done, ulong total)
            {
                WriteMessage(() =>
                {
                    m_writer.Write(ConsoleProgressMessage);
                    m_writer.Write(done);
                    m_writer.Write(total);
                });
            }

            private void WriteMessage(Action writeAction)
            {
                lock (m_lock)
                {
                    if (Volatile.Read(ref m_disabled))
                    {
                        return;
                    }

                    try
                    {
                        writeAction();
                        m_writer.Flush();
                    }
                    catch (IOException ex)
                    {
                        // When termination is requested (ctrl+break), the client is terminated and the pipe as well. 
                        // However, some threads in the server may still try to send something to client. 
                        // In that case, Flush will throw a 'Pipe is broken' exception and we can safely ignore those in case of cancellation.
                        if (!IsCancellationRequested)
                        {
                            // We know that the problem is in the console. No need to guess by calling AnalyzeExceptionRootCause
                            throw new BuildXLException(ex.Message, ExceptionRootCause.ConsoleNotConnected);
                        }
                    }
                }
            }

            /// <summary>
            /// Checks for a cancellation request.
            /// </summary>
            /// <remarks>
            /// There is an inharent race when client terminates and server terminates. The client sends a cancellation message, and then terminates
            /// when the message is read by the server. But before the server can set the <see cref="CancellationRequested"/> flag, there can be running threads trying to 
            /// write messages to the dead client. This method allows the <see cref="WriteMessage(Action)"/> to retry checking the <see cref="CancellationRequested"/>
            /// flag before determining that the problem is in the console.
            /// </remarks>
            private bool IsCancellationRequested
            {
                get
                {
                    const int SleepTimeMs = 100;

                    for (int i = 0; i < 3; ++i)
                    {
                        if (Volatile.Read(ref CancellationRequested))
                        {
                            return true;
                        }

                        Thread.Sleep(SleepTimeMs);
                    }

                    return false;
                }
            }

            public void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardText, string updatableText)
            {
                WriteMessage(() =>
                {
                    m_writer.Write(ConsoleTemporaryMessage);
                    m_writer.Write((byte)messageLevel);
                    m_writer.Write(standardText);
                    m_writer.Write(updatableText);
                });
            }

            public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine)
            {
                WriteMessage(() =>
                {
                    m_writer.Write(ConsoleTemporaryOnlyMessage);
                    m_writer.Write((byte)messageLevel);
                    m_writer.Write(standardLine);
                    m_writer.Write(overwritableLine);
                });
            }
        }

        #endregion

        #region Pipe support

        /// <summary>
        /// Waits for a named pipe client to connect, after which the pipe should be usable.
        /// The provided <paramref name="pipeName" /> must correspond to <paramref name="server" />.
        /// </summary>
        /// <remarks>
        /// This is a workaround for missing timeout / cancellation mechanics on <see cref="NamedPipeServerStream" />.
        /// We set a timer to abort waiting, and wake up the blocked thread by connecting a dummy client.
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static bool TryWaitForConnection(NamedPipeServerStream server, string pipeName, TimeSpan timeout)
        {
            const int Waiting = 0;
            const int Connected = 1;
            const int TimedOut = 2;
            int[] waitState = { Waiting };

            var idleTimer = new StoppableTimer(
                () =>
                {
                    if (Interlocked.CompareExchange(ref waitState[0], TimedOut, comparand: Waiting) == Waiting)
                    {
                        using (var pokeClient = new NamedPipeClientStream(
                            ".",
                            pipeName,
                            PipeDirection.InOut,
                            PipeOptions.None,
                            TokenImpersonationLevel.Identification,
                            HandleInheritability.None))
                        {
                            // Best-effort attempt to wake up server.WaitForConnection() below.
                            // We transitioned waitState to TimedOut, so this function is doomed to return false after-wakeup.
                            // WaitForConnection is likely but not definitely blocked at the instant we call Connect, so we should ignore failures.
                            try
                            {
                                pokeClient.Connect(1);
                            }
                            catch (TimeoutException)
                            {
                            }
                            catch (IOException)
                            {
                            }
                        }
                    }
                    else
                    {
                        Contract.Assume(waitState[0] == Connected);
                    }
                },
                dueTime: (int)timeout.TotalMilliseconds,
                period: 0);

            try
            {
                server.WaitForConnection();

                // Note that we may wake up from WaitForConnection, and then have a transition to the TimedOut state
                // (so timeout handling must not assume that we are waiting for a connection).
                int preConnectionState = Interlocked.CompareExchange(ref waitState[0], Connected, comparand: Waiting);
                if (preConnectionState != Waiting)
                {
                    Contract.Assume(preConnectionState == TimedOut);
                }
            }
            finally
            {
                idleTimer.Dispose();
            }

            return waitState[0] == Connected;
        }

        private static string GetPipeName(string uniqueAppName)
        {
            return "AppServer-" + uniqueAppName;
        }

        #endregion

        #region Client: Starting or connecting to an app-server

        public delegate Possible<Unit> TryStartServer(StartupParameters startupParams);

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public static Connection TryStartOrConnect(
            TryStartServer start,
            LightConfig lightConfig,
            string serverDeploymentPath,
            out ServerModeStatusAndPerf serverModeStatusAndPerf,
            out List<KeyValuePair<string, string>> variablesToPassThrough)
        {
            serverModeStatusAndPerf = default(ServerModeStatusAndPerf);

            AppDeployment clientApp;
            try
            {
                clientApp = AppDeployment.ReadDeploymentManifestFromRunningApp();
                serverModeStatusAndPerf.UpToDateCheck = new ServerDeploymentUpToDateCheck()
                                        {
                                            TimeToUpToDateCheckMilliseconds = (long)clientApp.ComputeTimestampBasedHashTime.TotalMilliseconds,
                                        };
            }
            catch (BuildXLException ex)
            {
                serverModeStatusAndPerf.ServerModeCannotStart = new ServerModeCannotStart()
                {
                    Kind = ServerCannotStartKind.Exception,
                    Reason = ex.GetLogEventMessage() + Environment.NewLine + ex.ToString(),
                };

                variablesToPassThrough = null;
                return null;
            }

            string uniqueAppName = GetStableAppServerName(lightConfig, clientApp.TimestampBasedHash, out variablesToPassThrough);

            // First, try to connect to an already-running server.
            NamedPipeClientStream clientStream = TryConnect(uniqueAppName);

            // As a fallback, try starting a new server.
            if (clientStream == null)
            {
                // Any exception trying to create the server deployment cache falls back to a local instance
                // An example of this is not having enough permissions to create the deployment cache folder
                ServerDeployment serverDeployment;
                try
                {
                    serverDeployment = ServerDeployment.GetOrCreateServerDeploymentCache(serverDeploymentPath, clientApp);
                    serverModeStatusAndPerf.CacheCreated = serverDeployment.CacheCreationInformation;
                }
                catch (Exception exception)
                {
                    ServerModeCannotStart cannotStartServer = new ServerModeCannotStart()
                                                              {
                                                                  Kind = ServerCannotStartKind.Exception,
                                                                  Reason = exception.GetLogEventMessage() + Environment.NewLine + exception.ToString()
                                                              };

                    serverModeStatusAndPerf.ServerModeCannotStart = cannotStartServer;

                    return null;
                }

                Assembly rootAssembly = Assembly.GetEntryAssembly();
                Contract.Assert(rootAssembly != null, "Could not look up entry assembly");

                string pathToProcess = Path.Combine(serverDeployment.DeploymentPath, new FileInfo(AssemblyHelper.GetAssemblyLocation(rootAssembly)).Name);
                if (pathToProcess.EndsWith(".dll"))
                {
                    pathToProcess = OperatingSystemHelper.IsUnixOS
                        ? Path.GetFileNameWithoutExtension(pathToProcess)
                        : Path.ChangeExtension(pathToProcess, "exe");
                }

                StartupParameters newServerParameters = StartupParameters.CreateForNewAppServer(
                    uniqueAppName,
                    clientApp.TimestampBasedHash.ToHex(),
                    pathToProcess,
                    lightConfig.ServerIdleTimeInMinutes);

                ServerModeCannotStart? cannotStart;
                if (newServerParameters.TryWaitForStartupComplete(start, TimeSpan.FromMilliseconds(StartupTimeoutInMilliseconds), out cannotStart))
                {
                    clientStream = TryConnect(uniqueAppName);
                }
                else
                {
                    serverModeStatusAndPerf.ServerModeCannotStart = cannotStart.Value;
                    return null;
                }
            }

            if (clientStream != null)
            {
                return new Connection(clientStream, BuildXLApp.CreateStandardConsole(lightConfig));
            }
            else
            {
                // The new server process didn't launch within the timeout. Return null so the wrapper can fall back on a local app instance.
                serverModeStatusAndPerf.ServerModeCannotStart = new ServerModeCannotStart()
                {
                    Kind = ServerCannotStartKind.Timeout,
                    Reason = "Connection to app server timed out",
                };

                return null;
            }
        }

        private static string GetStableAppServerName(LightConfig lightConfig, Fingerprint serverBinaryHash, out List<KeyValuePair<string, string>> variablesToPassThrough)
        {
            PathTranslator translator;
            PathTranslator.CreateIfEnabled(lightConfig.SubstTarget, lightConfig.SubstSource, out translator);

            using (var wrapper = Pools.StringBuilderPool.GetInstance())
            {
                const char Delimiter = '!';
                StringBuilder sb = wrapper.Instance;
                sb.Append(Environment.UserName);

                sb.Append(Delimiter);
                string baseDirectoryPath = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                sb.Append(translator?.Translate(baseDirectoryPath) ?? baseDirectoryPath);

                sb.Append(Delimiter);
                sb.Append(lightConfig.Config);

                // Stable app server depends on the hash of the content of BuildXL binaries
                sb.Append(Delimiter);
                sb.Append(serverBinaryHash.ToHex());

                // The identity needs to include whether the server process is elevated in case the client switches elevation
                sb.Append("Elevated:" + CurrentProcess.IsElevated);
                
                // Include HashType because client may switch between VSO or Dedup hashes
                sb.Append(Delimiter);
                sb.Append("EnableDedup:" + lightConfig.EnableDedup);

                // Need to include environment variables in the app server identity in case they change between runs.
                //
                // This is necessary since BuildXL checks environment variables in various places. It could be avoided by
                // passing relevant variables to the server process and having it query values from what was passed in,
                // but there are many call sites so doing so is cumbersome and variable changes between runs probably doesn't
                // happen often.
                //
                // Some environment variables may change the behavior of already loaded components in the server process
                // and may not simply be passed through. These get reflected in the AppServer name and will cause a new
                // server to be launched if they are changed.
                IDictionary environmentVariables = Environment.GetEnvironmentVariables();
                SortedList<string, string> variablesImpactingName = new SortedList<string, string>(environmentVariables.Count, StringComparer.Ordinal);
                variablesToPassThrough = new List<KeyValuePair<string, string>>();

                foreach (DictionaryEntry de in environmentVariables)
                {
                    var variableName = de.Key.ToString();
                    var variableValue = de.Value.ToString();
                    if (IsEnvironmentVariablePassThrough(variableName))
                    {
                        variablesToPassThrough.Add(new KeyValuePair<string, string>(variableName, variableValue));
                    }
                    else
                    {
                        variablesImpactingName.Add(variableName, variableValue);
                    }
                }

                foreach (var kvp in variablesImpactingName)
                {
                    sb.Append(kvp.Key.Length);
                    sb.Append(kvp.Key);
                    sb.Append(kvp.Value.Length);
                    sb.Append(kvp.Value);
                    sb.Append(Delimiter);
                }

                return MurmurHash3.Create(Encoding.UTF8.GetBytes(wrapper.Instance.ToString()), 0).ToString();
            }
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static NamedPipeClientStream TryConnect(string uniqueAppName)
        {
            NamedPipeClientStream pipe = null;
            string pipeName = GetPipeName(uniqueAppName);

            try
            {
                pipe = new NamedPipeClientStream(
                    ".",
                    pipeName,
                    PipeDirection.InOut,

                    // We want to be able to write a cancel message even while blocked on reading the next message from the server (e.g. console line).
                    PipeOptions.Asynchronous,
                    TokenImpersonationLevel.Identification,
                    HandleInheritability.None);

                try
                {
                    pipe.Connect(ConnectTimeoutInMilliseconds);
                }
                catch (TimeoutException)
                {
                    // TODO: We hit this case, unfortunately, for the 'pipe busy' case as well as the 'no such pipe' case sometimes.
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }

                // Read 'hello' byte: A server promise that it is not shutting down.
                int hello = pipe.ReadByte();
                if (hello == -1)
                {
                    // Pipe disconnected (server shutting down?)
                    return null;
                }

                if (hello != ServerHelloMessage)
                {
                    return null;
                }

                return pipe;
            }
            catch (Exception)
            {
                if (pipe != null)
                {
                    pipe.Dispose();
                }

                throw;
            }
        }

        /// <summary>
        /// Client connection to an app server. Forwards arguments, and receives console output and eventually exit status.
        /// </summary>
        public sealed class Connection : IDisposable
        {
            private readonly NamedPipeClientStream m_clientStream;
            private readonly IConsole m_console;

            internal Connection(NamedPipeClientStream clientStream, IConsole console)
            {
                Contract.Requires(clientStream != null);
                Contract.Requires(console != null);

                m_console = console;
                m_clientStream = clientStream;
            }

            /// <summary>
            /// Run
            /// </summary>
            /// <exception cref="BuildXLException">
            /// Thrown if the named pipe stream gets unexpectedly disconnected, or any other I/O issue arises.
            /// </exception>
            public ExitKind RunWithArgs(
                IReadOnlyList<string> rawArgs,
                List<KeyValuePair<string, string>> environmentVariables,
                ServerModeStatusAndPerf serverModeStatusAndPerf)
            {
                Contract.Requires(rawArgs != null);

                return ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        // Phase 1: We send data to the server
                        using (var bufferedStream = new BufferedStream(m_clientStream))
                        using (var writer = new BinaryWriter(bufferedStream, Encoding.UTF8, leaveOpen: true))
                        {
                            // Write data to server.
                            BuildXLAppServerData.Create(
                                rawArgs,
                                environmentVariables,
                                serverModeStatusAndPerf).Serialize(writer);

                            bool cancellationRequested = false;

                            ConsoleCancelEventHandler cancelKeyPressHandler = (sender, args) =>
                            {
                                try
                                {
                                    writer.Write(args.SpecialKey == ConsoleSpecialKey.ControlC ? CancelMessage : TerminateMessage);
                                    writer.Flush();

                                    Volatile.Write(ref cancellationRequested, true);
                                    Thread.Sleep(100);

                                    // Wait until the server read the cancellation message.
                                    // If we don't wait for the pipe to drain, the server may throw a broken pipe exception when trying to read the cancel/terminate message.
                                    m_clientStream.WaitForPipeDrain();

                                    args.Cancel = args.SpecialKey == ConsoleSpecialKey.ControlC;
                                }
#pragma warning disable ERP022 // TODO: This should really handle specific errors
                                catch
                                {
                                    // It is OK to run the cancel handler multiple times, so swallow all exceptions under the assumption that the user will attempt Control-C again.
                                    // The server relies on seeing the client write a cancel message to cancel the build. It is better to swallow the exception and retry than
                                    // have the client crash before it can write the message.

                                    // Known exceptions:
                                    // 1. Developers can press multiple Control-C so there is a slight chance for the client to talk to the disposed pipe stream in the non-first Control-C handlers.
                                    //    Writing to a disposed pipe stream will throw an exception (e.g., pipe is broken) so we swallow it here.
                                    // 2. Read bytes left in BufferedStream when trying to write the cancellation message causes
                                    //    System.NotSupportedException: Cannot write to a BufferedStream while the read buffer is not empty if the underlying stream is not seekable. 
                                    //    Ensure that the stream underlying this BufferedStream can seek or avoid interleaving read and write operations on this BufferedStream.
                                }
                            };
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

                            Console.CancelKeyPress += cancelKeyPressHandler;

                            // Writer won't be disposed, because the registration is disposed first
                            // ReSharper disable once AccessToDisposedClosure
                            try
                            {
                                // Phase 2: We read data from the server
                                return ExceptionUtilities.HandleRecoverableIOException(
                                    () =>
                                    {
                                        using (var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true))
                                        {
                                            while (true)
                                            {
                                                byte messageId;
                                                try
                                                {
                                                    messageId = reader.ReadByte();
                                                }
                                                catch (EndOfStreamException)
                                                {
                                                    if (!Volatile.Read(ref cancellationRequested))
                                                    {
                                                        throw;
                                                    }

                                                    return ExitKind.Aborted;
                                                }

                                                switch (messageId)
                                                {
                                                    case ConsoleOutputMessage:
                                                        ReadAndForwardConsoleMessage(reader);
                                                        break;
                                                    case ConsoleProgressMessage:
                                                        ReadAndForwardProgress(reader);
                                                        break;
                                                    case ConsoleTemporaryMessage:
                                                        ReadAndForwardTemporaryMessage(reader);
                                                        break;
                                                    case ConsoleTemporaryOnlyMessage:
                                                        ReadAndForwardTemporaryOnlyMessage(reader);
                                                        break;
                                                    case ExitCodeMessage:
                                                        ExitKind exit;
                                                        if (!EnumTraits<ExitKind>.TryConvert(reader.ReadByte(), out exit))
                                                        {
                                                            exit = ExitKind.InternalError;
                                                        }

                                                        return exit;
                                                    default:
                                                        if (!Volatile.Read(ref cancellationRequested))
                                                        {
                                                            throw new BuildXLException("Unknown message received from app server");
                                                        }

                                                        break;
                                                }
                                            }
                                        }
                                    },
                                    ex => { throw new BuildXLException("Error while reading data from server process. Inner exception reason: " + ex?.Message, ex); });
                            }
                            finally
                            {
                                Console.CancelKeyPress -= cancelKeyPressHandler;
                            }
                        }
                    },
                    ex => { throw new BuildXLException("Error while writing data to server process. Inner exception reason: " + ex?.Message, ex); });
            }

            private void ReadAndForwardConsoleMessage(BinaryReader reader)
            {
                MessageLevel messageLevel;
                if (!EnumTraits<MessageLevel>.TryConvert(reader.ReadByte(), out messageLevel))
                {
                    throw new BuildXLException("Unknown console message level received from app server");
                }

                m_console.WriteOutputLine(messageLevel, reader.ReadString());
            }

            private void ReadAndForwardTemporaryMessage(BinaryReader reader)
            {
                MessageLevel messageLevel;
                if (!EnumTraits<MessageLevel>.TryConvert(reader.ReadByte(), out messageLevel))
                {
                    throw new BuildXLException("Unknown console message level received from app server");
                }

                m_console.WriteOverwritableOutputLine(messageLevel, reader.ReadString(), reader.ReadString());
            }

            private void ReadAndForwardTemporaryOnlyMessage(BinaryReader reader)
            {
                MessageLevel messageLevel;
                if (!EnumTraits<MessageLevel>.TryConvert(reader.ReadByte(), out messageLevel))
                {
                    throw new BuildXLException("Unknown console message level received from app server");
                }

                m_console.WriteOverwritableOutputLineOnlyIfSupported(messageLevel, reader.ReadString(), reader.ReadString());
            }

            private void ReadAndForwardProgress(BinaryReader reader)
            {
                ulong done = reader.ReadUInt64();
                ulong total = reader.ReadUInt64();

                m_console.ReportProgress(done, total);
            }

            public void Dispose()
            {
                m_clientStream.Dispose();
                m_console.Dispose();
            }
        }

        #endregion

        /// <summary>
        /// App-server specific parameters, possibly provided by a client starting a server on demand.
        /// These parameters are needed for a startup-handshake as a client waits for an app server to start
        /// (consider a client which starts the app server executable and wants to immediately connect).
        /// </summary>
        public sealed class StartupParameters
        {
            private const string StartupEventNamePrefix = "DominoAppServerStart-";
            private readonly Guid m_eventId;

            // Used to ensure the failure signal is only triggered before the startup signal. This does not need to be
            // serialized between client and server
            private bool m_signaledStartupAttemptComplete;

            private StartupParameters(Guid guid, string uniqueAppServerName, string timestampBasedHash, string pathToProcess, int serverMaxIdleTimeInMinutes)
            {
                Contract.Requires(!string.IsNullOrEmpty(uniqueAppServerName));
                Contract.Requires(!string.IsNullOrEmpty(pathToProcess));
                Contract.Requires(serverMaxIdleTimeInMinutes > 0);

                UniqueAppServerName = uniqueAppServerName;
                m_eventId = guid;
                PathToProcess = pathToProcess;
                ServerMaxIdleTimeInMinutes = serverMaxIdleTimeInMinutes;
                TimestampBasedHash = timestampBasedHash;
            }

            /// <summary>
            /// Creates startup parameters that can be used for a new app server instance.
            /// </summary>
            public static StartupParameters CreateForNewAppServer(string uniqueAppServerName, string timestampBasedHash, string pathToProcess, int serverMaxIdleTimeInMinutes)
            {
                Contract.Requires(!string.IsNullOrEmpty(uniqueAppServerName));
                Contract.Requires(!string.IsNullOrEmpty(pathToProcess));
                Contract.Requires(serverMaxIdleTimeInMinutes > 0);

                return new StartupParameters(Guid.NewGuid(), uniqueAppServerName, timestampBasedHash, pathToProcess, serverMaxIdleTimeInMinutes);
            }

            /// <summary>
            /// Attempts to parse a string representation as generated by <see cref="ToString" />
            /// </summary>
            public static StartupParameters TryParse(string value)
            {
                string[] parts = value.Split(new[] { '|' }, 5);
                if (parts.Length != 5)
                {
                    return null;
                }

                string eventGuidHex = parts[0];
                if (string.IsNullOrEmpty(eventGuidHex))
                {
                    return null;
                }

                Guid guid;
                if (!Guid.TryParseExact(eventGuidHex, "N", out guid))
                {
                    return null;
                }

                string appName = parts[1];
                if (string.IsNullOrEmpty(appName))
                {
                    return null;
                }

                string timestampBasedHash = parts[2];
                if (string.IsNullOrEmpty(timestampBasedHash))
                {
                    return null;
                }

                string pathToProcess = parts[3];
                if (string.IsNullOrEmpty(pathToProcess))
                {
                    return null;
                }

                int serverMaxIdleTimeInMinutes;
                if (!int.TryParse(parts[4], out serverMaxIdleTimeInMinutes) || serverMaxIdleTimeInMinutes <= 0)
                {
                    return null;
                }

                return new StartupParameters(guid, appName, timestampBasedHash, pathToProcess, serverMaxIdleTimeInMinutes);
            }

            /// <summary>
            /// Returns a string representation suitable for <see cref="TryParse" />
            /// </summary>
            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:N}|{1}|{2}|{3}|{4}", m_eventId, UniqueAppServerName, TimestampBasedHash, PathToProcess, ServerMaxIdleTimeInMinutes);
            }

            /// <summary>
            /// Name which identifies the app server (per name, one app server may be running).
            /// </summary>
            public string UniqueAppServerName { get; }

            /// <summary>
            /// Timestamp based hash of the deployment.
            /// </summary>
            public string TimestampBasedHash { get; }

            /// <summary>
            /// Server max idle time in minute.
            /// </summary>
            public int ServerMaxIdleTimeInMinutes { get; }

            private string GetStartupAttemptedEventName()
            {
                return StartupEventNamePrefix + m_eventId.ToString("N");
            }

            private string GetStartupFailedEventName()
            {
                return GetStartupAttemptedEventName() + "Failed";
            }

            /// <summary>
            /// Path to the app server process
            /// </summary>
            public string PathToProcess { get; }

            /// <summary>
            /// Calls <paramref name="startup" />, which is responsible for starting an app server with these parameters,
            /// and waits for the app server to start. It is an error for more than one process to wait on the same <see cref="StartupParameters" />.
            /// </summary>
            public bool TryWaitForStartupComplete(TryStartServer startup, TimeSpan timeout, out ServerModeCannotStart? failureToStart)
            {
                Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn<ServerModeCannotStart?>(out failureToStart).HasValue);

                bool eventIsNew;
                using (
                    var startupCompletedEvent = new EventWaitHandle(
                        initialState: false,
                        mode: EventResetMode.ManualReset,
                        name: GetStartupAttemptedEventName(),
                        createdNew: out eventIsNew))
                {
                    Contract.Assume(eventIsNew);
                    using (
                        var startupFailedEvent = new EventWaitHandle(
                            initialState: false,
                            mode: EventResetMode.ManualReset,
                            name: GetStartupFailedEventName(),
                            createdNew: out eventIsNew))
                    {
                        Possible<Unit> maybeProcessStarted = startup(this);
                        if (!maybeProcessStarted.Succeeded)
                        {
                            failureToStart = new ServerModeCannotStart()
                            {
                                Kind = ServerCannotStartKind.ServerProcessCreationFailed,
                                Reason = maybeProcessStarted.Failure.DescribeIncludingInnerFailures(),
                            };

                            return false;
                        }

                        bool started = startupCompletedEvent.WaitOne(timeout);
                        if (started)
                        {
                            if (!startupFailedEvent.WaitOne(0))
                            {
                                failureToStart = null;
                                return true;
                            }
                            else
                            {
                                failureToStart = new ServerModeCannotStart()
                                {
                                    Kind = ServerCannotStartKind.ServerFailedToStart,
                                    Reason = Strings.Server_FailedToStart,
                                };
                                return false;
                            }
                        }
                        else
                        {
                            failureToStart = new ServerModeCannotStart()
                            {
                                Kind = ServerCannotStartKind.Timeout,
                                Reason = Strings.Server_StartupTimeoutReached,
                            };
                            return false;
                        }
                    }
                }
            }

            /// <summary>
            /// Signals any waiter on these same startup parameters that an app server for these parameters has finished
            /// attempting to start
            /// </summary>
            public void SignalStartupAttemptComplete()
            {
                using (
                    var startupEvent = new EventWaitHandle(
                        initialState: false,
                        mode: EventResetMode.ManualReset,
                        name: GetStartupAttemptedEventName()))
                {
                    startupEvent.Set();
                    m_signaledStartupAttemptComplete = true;
                }
            }

            /// <summary>
            /// Signals any waiter on these same startup parameters that an app server for these parameters failed to start
            /// </summary>
            public void SignalStartupFailed()
            {
                Contract.Assert(!m_signaledStartupAttemptComplete, "Failure signal may not be set after Startup signal.");

                using (
                    var failedEvent = new EventWaitHandle(
                        initialState: false,
                        mode: EventResetMode.ManualReset,
                        name: GetStartupFailedEventName()))
                {
                    failedEvent.Set();
                }
            }
        }

        /// <inheritdoc />
        public void StartRun(LoggingContext loggingContext)
        {
            m_startSnapshot = PerformanceSnapshot.CreateFromCurrentProcess();
            ServerModeBuildStarted start = new ServerModeBuildStarted()
                                           {
                                               PID = m_startSnapshot.PID,
                                               StartPerformance = m_startSnapshot,
                                               TimesPreviouslyUsed = m_timesUsed,
                                               TimeIdleSeconds = (int)m_idleTime.Elapsed.TotalSeconds,
                                           };

            if (m_timesUsed == 0)
            {
                Logger.Log.StartingNewServer(loggingContext, start);
            }
            else
            {
                Logger.Log.UsingExistingServer(loggingContext, start);
            }

            Contract.Assume(m_startupParameters != null, "StartupParameters should have previously been set.");
            Logger.Log.ServerModeBuildStarted(loggingContext, start, m_startupParameters.UniqueAppServerName);

            m_timesUsed++;
        }

        /// <inheritdoc />
        public void EndRun(LoggingContext loggingContext)
        {
            PerformanceSnapshot stopSnapshot = PerformanceSnapshot.CreateFromCurrentProcess();
            Logger.Log.ServerModeBuildCompleted(
                loggingContext,
                new ServerModeBuildCompleted()
                {
                    EndPerformance = stopSnapshot,
                    PerformanceDifference = PerformanceSnapshot.Compare(m_startSnapshot, stopSnapshot),
                });
        }

        public void Dispose()
        {
            m_engineState?.Dispose();
        }

        /// <inheritdoc/>
        public bool ShutDownTelemetryAfterRun => false;
    }
}
