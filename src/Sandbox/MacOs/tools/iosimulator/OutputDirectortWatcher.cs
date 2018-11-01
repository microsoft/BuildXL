// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace IOSimulator
{
        /// <summary>
        /// An OutputDirectoryWatcher is initialized with a path to a directory and uses a file watcher to
        /// observe any directory or file changes within that directory that match a given pattern. Also calculates
        /// MD5 hashes for the file change events only to simulate I/O load.
        /// </summary>
        public class OutputDirectortWatcher
    {
        private FileSystemWatcher watcher;
        private List<string> hashes;
        public int filesHashed;

        public OutputDirectortWatcher(string outputDirectoryPath)
        {
            FileAttributes attr = File.GetAttributes(outputDirectoryPath);
            if (!attr.HasFlag(FileAttributes.Directory))
            {
                throw new Exception("Only paths to directories should be used to initialize an OutputDirectortWatcher!");
            }

            hashes = new List<string>();
            filesHashed = 0;

            watcher = new FileSystemWatcher();
            watcher.Path = outputDirectoryPath;
            watcher.IncludeSubdirectories = true;
            watcher.NotifyFilter =
                NotifyFilters.CreationTime |
                NotifyFilters.LastAccess |
                NotifyFilters.LastWrite |
                NotifyFilters.FileName |
                NotifyFilters.DirectoryName;

            watcher.Filter = IOSimulatorApp.ObserverPattern;
        }

        public void Start()
        {
            watcher.Created += new FileSystemEventHandler(this.OnChanged);
            watcher.Changed += new FileSystemEventHandler(this.OnChanged);

            // We currently just track file creations and changes in the specified directory and calculate hashes for those
            // enable if more is needed

            // watcher.Deleted += new FileSystemEventHandler(this.OnDeleted);
            // watcher.Renamed += new RenamedEventHandler(this.OnRenamed);

            watcher.EnableRaisingEvents = true;
        }

        // Define the event handlers.
        public void OnChanged(object source, FileSystemEventArgs e)
        {
            try
            {
                FileAttributes attr = File.GetAttributes(e.FullPath);
                if (!attr.HasFlag(FileAttributes.Directory))
                {
                    // Go hash the output change
                    Task.Run(async () =>
                    {
                        using (FileStream SourceStream = File.Open(e.FullPath, FileMode.Open))
                        {
                            var result = new byte[SourceStream.Length];
                            await SourceStream.ReadAsync(result, 0, (int)SourceStream.Length);

                            if (Hashing.HashByteArray(ref result, out var hash, verbose: IOSimulatorApp.Verbose))
                            {
                                filesHashed++;

                                if (IOSimulatorApp.Logging)
                                {
                                    using (StreamWriter sw = File.AppendText(IOSimulatorApp.OutputHashesLogPath))
                                    {
                                        sw.WriteLine("[{0} - Observer] Hashed: {1} with {2}", DateTime.Now, e.FullPath, hash);
                                    }
                                }
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                if (IOSimulatorApp.Verbose) Console.WriteLine(ex.ToString());
            }
        }
    }
}
