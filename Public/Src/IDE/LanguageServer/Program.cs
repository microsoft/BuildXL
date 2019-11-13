// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using BuildXL.Utilities;

using BuildXL.Ide.JsonRpc;
using System.Net.Sockets;

namespace BuildXL.Ide.LanguageServer
{
    /// <nodoc />
    public static class Program
    {
        /// <summary>
        /// The command line argument the pipe passed to us from VSCode is --pipe=\\.\pipe\pipeName.
        /// </summary>
        /// <remarks>
        /// The \\.\ is the server name (. for local), then the transport, which is a named pipe, followed
        /// by the pipe name.
        /// Since we only ever support local (the .) and named pipes, then we will look for this string
        /// to begin with this prefix, and only accept that prefix.
        /// Then we need to parse off the remainder of the string so we can use it with <see cref="NamedPipeClientStream"/>.
        /// </remarks>
        private static string s_localPipeNamePrefix = @"\\.\pipe\";

        /// <nodoc />
        public static void Main(string[] args)
        {
            string pipeName = null;
            string sockFile = null;
            
            if (args != null)
            {
                foreach (var argument in args)
                {
                    var split = argument.Split('=');
                    if (split.Length == 2)
                    {
                        if (split[0].Equals("--pipe", StringComparison.Ordinal))
                        {
                            if (!OperatingSystemHelper.IsUnixOS)
                            {
                                if (split[1].StartsWith(s_localPipeNamePrefix, StringComparison.Ordinal))
                                {
                                    pipeName = split[1].Substring(s_localPipeNamePrefix.Length);
                                }
                            }
                            else
                            {
                                // The generated named pipe from the NodeJS language server on Unix looks something like
                                // /var/run/CoreFxPipe_3a94898ed89 - we split by DirectorySeparatorChar to get the last
                                // part, then split by '_' to get the hex value and use it as pipe name. CoreFx
                                // hardcodes the 'CoreFxPipe_' part so the socket must be named like that when its created
                                if (split[1].Contains("CoreFxPipe_"))
                                {
                                    pipeName = split[1].Split(Path.DirectorySeparatorChar).Last().Split('_')[1];
                                }
                                else if (split[1].EndsWith(".sock"))
                                {
                                    sockFile = split[1];
                                }
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(pipeName) && string.IsNullOrEmpty(sockFile))
            {
                throw new ArgumentException("Neither a pipe name nor a .sock file was passed on the command line.");
            }
            
            // Each concurrently running instance of the server should get its own log file. But we don't want the number
            // of log files to grow too much, so we reuse the names based on the number of instances running at a given
            // time. So given there are n vscode processes running right now, we will find a number 1..n that hasn't been claimed yet.
            var processCount = Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length;
            var pathToLog = string.Empty;

            for (int i = processCount; i > 0; i--)
            {
                var maybeLogPath = Path.Combine(Path.GetTempPath(), "LanguageService_" + i + ".log");
                if (!File.Exists(maybeLogPath))
                {
                    pathToLog = maybeLogPath;
                    break;
                }
            }

            // As a last resort (if somehow all log file names were claimed, we will use processId for unique seed)
            if (string.IsNullOrEmpty(pathToLog))
            {
                pathToLog = Path.Combine(Path.GetTempPath(), "LanguageService_" + Process.GetCurrentProcess().Id + ".log");
            }

            Connect(pipeName, sockFile, (stream) =>
            {
                using (var app = new App(stream, stream, pathToLog))
                {
                    app.WaitForExit();
                }
            });
        }

        private static void Connect(string pipeName, string unixSockFilePath, Action<Stream> act)
        {
            if (pipeName != null)
            {
                ConnectWithNamedPipe(pipeName, act);
            }
            else if (unixSockFilePath != null)
            {
                ConnectWithUnixSocket(unixSockFilePath, act);
            }
            else
            {
                throw new Exception("Neither pipe nor sock was provided");
            }
        }

        private static void ConnectWithUnixSocket(string sockName, Action<Stream> act)
        {
#if PLATFORM_WIN
            throw new NotImplementedException("Unix socket files can only be used on unix operating systems");
#else
            using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP))
            {
                socket.Connect(new System.Net.UnixEndPoint(sockName));
                using (var stream = new NetworkStream(socket))
                {
                    act(stream);
                }
            }
#endif
        }

        private static void ConnectWithNamedPipe(string pipeName, Action<Stream> act)
        {
            using (var pipeStream = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                // The connect call will throw if the connection cannot be established within 5 seconds.
                pipeStream.Connect(5000);
                act(pipeStream);
            }
        }
    }
}
