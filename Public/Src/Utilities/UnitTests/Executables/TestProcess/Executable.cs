// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace Test.BuildXL.Executables.TestProcess
{
    /// <summary>
    /// Encapsulates an executable process that can perform filesystem operations
    /// </summary>
    /// <remarks>
    /// Keep this process simple and avoid referencing complex datastructures like the PathTable to avoid JIT overhead.
    /// </remarks>
    public sealed class Executable
    {
        // Queue of ops the process will run
        private Queue<Operation> m_opQueue = new Queue<Operation>();

        /// <summary>
        /// Creates a Process with the given args converted to <see cref="Operation"/>s and queued to run
        /// </summary>
        public Executable(string[] args)
        {
            // Add all the process operations in order to queue
            for (int i = 0; i < args.Length; i++)
            {
                m_opQueue.Enqueue(Operation.CreateFromCommandLine(args[i]));
            }
        }

        /// <summary>
        /// Runs all operations in process queue
        /// </summary>
        public void Run()
        {
            while (m_opQueue.Count != 0)
            {
                m_opQueue.Dequeue().Run();
            }

            return;
        }
    }
}
