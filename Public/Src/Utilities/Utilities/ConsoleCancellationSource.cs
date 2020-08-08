// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Wrapper around managing the event handlers for Ctrl + C
    /// </summary>
    public class ConsoleCancellationSource : IDisposable
    {
        private readonly CancellationTokenSource m_cts = new CancellationTokenSource();
        private readonly ConsoleCancelEventHandler m_handler;

        /// <nodoc />
        public CancellationToken Token { get; }

        /// <nodoc />
        public ConsoleCancellationSource()
        {
            m_handler = new ConsoleCancelEventHandler((sender, args) =>
            {
                m_cts.Cancel();
                args.Cancel = true;
            });

            Console.CancelKeyPress += m_handler;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Console.CancelKeyPress -= m_handler;
            m_cts.Dispose();
        }
    }
}
