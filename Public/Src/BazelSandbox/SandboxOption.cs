// Copyright 2019 The Bazel Authors. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BuildXL.Processes;
using BuildXL.Utilities;

namespace Bazel {
    /// <summary>
    /// Class to store sandbox configuration
    /// </summary>
    public class SandboxOptions {
        private static uint kInfiniteTime = 0xffffffff;

        /// <summary>
        /// Working directory (-W)
        /// </summary>
        public AbsolutePath working_dir { get; private set; } = AbsolutePath.Invalid;

        /// <summary>
        /// How long to wait before killing the child (-T)
        /// </summary>
        public uint timeout_secs { get; private set; } = kInfiniteTime;
        /// <summary>
        /// How long to wait before sending SIGKILL in case of timeout (-t)
        /// </summary>
        public uint kill_delay_secs { get; private set; } = kInfiniteTime;
        /// <summary>
        /// Where to redirect stdout (-l)
        /// </summary>
        public AbsolutePath stdout_path { get; private set; } = AbsolutePath.Invalid;
        /// <summary>
        /// Where to redirect stderr (-L)
        /// </summary> 
        public AbsolutePath stderr_path { get; private set; } = AbsolutePath.Invalid;
        /// <summary>
        /// Files or directories to make writable for the sandboxed process (-w)
        /// </summary>
        public List<AbsolutePath> writable_files { get; private set; } = new List<AbsolutePath>();
        // Directories where to mount an empty tmpfs (-e)
        // public List<string> tmpfs_dirs;
        /// <summary>
        /// Source of files or directories to explicitly bind mount in the sandbox (-M)
        /// </summary>
        public List<AbsolutePath> bind_mount_sources { get; private set; } = new List<AbsolutePath>();
        /// <summary>
        /// Target of files or directories to explicitly bind mount in the sandbox (-m)
        /// </summary> 
        public List<AbsolutePath> bind_mount_targets { get; private set; } = new List<AbsolutePath>();
        // Where to write stats, in protobuf format (-S)
        // public AbsolutePath stats_path { get; private set; } = AbsolutePath.Invalid;
        // Set the hostname inside the sandbox to 'localhost' (-H)
        // public bool fake_hostname;
        // Create a new network namespace (-N)
        // public bool create_netns;
        // Pretend to be root inside the namespace (-R)
        // public bool fake_root;
        // Set the username inside the sandbox to 'nobody' (-U)
        // public bool fake_username;
        // Print debugging messages (-D)
        // public bool debug;
        /// <summary>
        /// Command to run (--)
        /// </summary>
        public List<string> args { get; private set; } = null;

        /// <summary>
        /// Default constructor
        /// </summary>
        public SandboxOptions() { }

        /// <summary>
        /// Parse args into SandboxOptions
        /// </summary>
        /// <param name="args"></param>
        /// <param name="pathTable"></param>
        public void ParseOptions(string[] args, PathTable pathTable)
        {
            Console.WriteLine(args.ToString());
            int i = 0;
            for (; i < args.Length && args[i] != "--"; i++)
            {
                var arg = args[i];
                if (arg.Length > 1 && (arg[0] == '/' || arg[0] == '-'))
                {
                    var name = arg.Substring(1);
                    switch (name)
                    {
                        case "W":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.working_dir = path;
                                break;
                            }
                        case "T":
                            {
                                try
                                {
                                    this.timeout_secs = Convert.ToUInt32(args[++i]);
                                }
                                catch (Exception e)
                                {
                                    ExitWithError($"{args[i]} is not valid number:\n{e.ToString()}");

                                }
                                break;
                            }
                        case "t":
                            {
                                try
                                {
                                    this.kill_delay_secs = Convert.ToUInt32(args[++i]);
                                }
                                catch (Exception e)
                                {
                                    ExitWithError($"{args[i]} is not valid number:\n{e.ToString()}");
                                }
                                break;
                            }
                        case "l":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.stdout_path = path;
                                break;
                            }
                        case "L":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.stderr_path = path;
                                break;
                            }
                        case "w":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.writable_files.Add(path);
                                break;
                            }
                        case "M":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.bind_mount_sources.Add(path);
                                break;
                            }
                        case "m":
                            {
                                var path = AbsolutePath.Invalid;
                                if (!AbsolutePath.TryCreate(pathTable, args[++i], out path))
                                {
                                    ExitWithError($"Cannot create absolute path from '{args[i]}'");
                                }
                                this.bind_mount_targets.Add(path);
                                break;
                            }
                        default:
                            ExitWithError($"Unknown option: {arg}");
                            break;
                    }
                }
                else if (args.Length > 1 && arg[0] == '@')
                {
                    ExitWithError("Param file handling not yet implemented");
                }
                else
                {
                    ExitWithError($"Unknown argument: {arg}");
                }
            }
            if (args[i] != "--" || i == args.Length)
            {
                ExitWithError("Command to sandboxed not specified");
            }
            this.args = args.Skip(i + 1).ToList();
        }

        private void ExitWithError(string msg) {
            Console.WriteLine(msg);
            PrintUsage();
        }

        private void PrintUsage() {
            var processName = Process.GetCurrentProcess().ProcessName;
            Console.Write(
                    $"\nUsage: {processName} -- command arg1 @args\n" +
                    "\nPossible arguments:\n" +
                    "  -W <working-dir>  working directory (uses current directory if " +
                    "not specified)\n" +
                    "  -T <timeout>  timeout after which the child process will be " +
                    "terminated with SIGTERM\n" +
                    "  -t <timeout>  in case timeout occurs, how long to wait before " +
                    "killing the child with SIGKILL\n" +
                    "  -l <file>  redirect stdout to a file\n" +
                    "  -L <file>  redirect stderr to a file\n" +
                    "  -w <file>  make a file or directory writable for the sandboxed " +
                    "process\n" +
                    "  -e <dir>  mount an empty tmpfs on a directory\n" +
                    "  -M/-m <source/target>  directory to mount inside the sandbox\n" +
                    "    Multiple directories can be specified and each of them will be " +
                    "mounted readonly.\n" +
                    "    The -M option specifies which directory to mount, the -m option " +
                    "specifies where to\n" +
                    "  -S <file>  if set, write stats in protobuf format to a file\n" +
                    "  -H  if set, make hostname in the sandbox equal to 'localhost'\n" +
                    "  -N  if set, a new network namespace will be created\n" +
                    "  -R  if set, make the uid/gid be root\n" +
                    "  -U  if set, make the uid/gid be nobody\n" +
                    "  -D  if set, debug info will be printed\n" +
                    "  @FILE  read newline-separated arguments from FILE\n" +
                    "  --  command to run inside sandbox, followed by arguments\n");
            Environment.Exit(1);
        }
    }
}
