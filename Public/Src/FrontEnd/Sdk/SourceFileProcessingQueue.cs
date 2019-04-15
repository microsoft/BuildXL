// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Utilities.Tasks;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Convers parsed file (<see cref="ISourceFile"/> into evaluation representations.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public sealed class SourceFileProcessingQueue<TParseResult>
    {
        // The queue for concurrent file processing
        private readonly ActionBlock<QueueInput<TParseResult>> m_parseQueue;

        /// <summary>
        /// Creates a module parsing queue. The queue options are specified by the provided queueOptions.
        /// </summary>
        public SourceFileProcessingQueue(int degreeOfParallelism)
        {
            Contract.Requires(degreeOfParallelism >= 1);

            m_parseQueue = new ActionBlock<QueueInput<TParseResult>>(
                ProcessWorkItem,
                dataflowBlockOptions: new ExecutionDataflowBlockOptions()
                {
                    MaxDegreeOfParallelism = degreeOfParallelism,
                });
        }

        /// <summary>
        /// Converts a given source file into evaluation representation.
        /// </summary>
        public Task<TParseResult> ProcessFileAsync(ISourceFile sourceFile, Func<ISourceFile, Task<TParseResult>> parseFunc)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(parseFunc != null);

            var item = new QueueInput<TParseResult>(sourceFile, parseFunc);

            bool postResult = m_parseQueue.Post(item);
            Contract.Assert(postResult, "m_parseQueue.Post should return true.");

            // the queue handler ('ProcesWorkItem' method) completes this task
            return item.TaskSource.Task;
        }

        private static async Task ProcessWorkItem(QueueInput<TParseResult> parseInput)
        {
            try
            {
                var result = await parseInput.ParseFile(parseInput.SourceFile);
                parseInput.TaskSource.SetResult(result);
            }
            catch (Exception e)
            {
                parseInput.TaskSource.SetException(e);
            }
        }

        private readonly struct QueueInput<TParseInputResult>
        {
            public readonly ISourceFile SourceFile;
            public readonly Func<ISourceFile, Task<TParseInputResult>> ParseFile;
            public readonly TaskSourceSlim<TParseInputResult> TaskSource;

            public QueueInput(ISourceFile sourceFile, Func<ISourceFile, Task<TParseInputResult>> parseFile)
            {
                SourceFile = sourceFile;
                ParseFile = parseFile;
                TaskSource = TaskSourceSlim.Create<TParseInputResult>();
            }
        }
   }
}
