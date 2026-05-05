// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Utilities.Core;

namespace BuildXL.Processes
{
    /// <summary>
    /// Handler for stdout and stderr incremental output from a sandboxed process.
    /// Calls a stream observer if configured, and stores the first maxMemoryLength characters
    /// in a memory buffer (StringBuilder). If stream redirection is configured and the memory
    /// buffer overflows, writes the stream to a backing file.
    /// </summary>
    /// <remarks>
    /// Concurrency model:
    ///   - <see cref="AppendLine"/> is called by the pipe-reader thread (single writer).
    ///   - <see cref="Freeze"/> / <see cref="Dispose"/> are called by the process-exit /
    ///     teardown thread (single closer).
    ///
    /// On the kill / cancel teardown path, an in-flight pipe-reader callback can invoke
    /// <see cref="AppendLine"/> concurrently with <see cref="Freeze"/> if the caller does
    /// not first await pipe-reader completion. To stay correct under that race, this class
    /// uses a state latch (<c>m_state</c>) plus an in-flight-AppendLine counter
    /// (<c>m_inFlightAppendLines</c>) so that:
    ///   1. <see cref="AppendLine"/> reserves a slot via <see cref="Interlocked.Increment(ref int)"/>,
    ///      then checks the state. If the builder has been latched, the call early-returns
    ///      without mutating buffers.
    ///   2. <see cref="Freeze"/> / <see cref="Dispose"/> latch the state via
    ///      <see cref="Interlocked.Exchange(ref int, int)"/> and then spin-wait for any
    ///      in-flight <see cref="AppendLine"/> to finish before snapshotting state.
    ///
    /// The hot path (<see cref="AppendLine"/>) adds two interlocked operations and one
    /// volatile read; no allocations.
    /// </remarks>
    internal sealed class SandboxedProcessOutputBuilder : IDisposable
    {
        private const int StateAccepting = 0;
        private const int StateLatched = 1;

        private readonly SandboxedProcessFile m_file;
        private readonly ISandboxedProcessFileStorage m_fileStorage;
        private readonly int m_maxMemoryLength;
        private readonly Action<string> m_observer;

        private string m_fileName;
        private long m_length;
        private readonly PooledObjectWrapper<StringBuilder> m_stringBuilderWrapper;
        private StringBuilder m_stringBuilder;
        private TextWriter m_textWriter;
        private BuildXLException m_exception;

        // 0 = Accepting (AppendLine may mutate buffers), 1 = Latched (Freeze / Dispose
        // is in progress or has completed; AppendLine must early-return without mutating).
        // Mutated only via Interlocked.Exchange / read via volatile semantics.
        private volatile int m_state = StateAccepting;

        // Number of AppendLine calls currently between the Interlocked.Increment slot
        // reservation and the matching Interlocked.Decrement. Freeze / Dispose drain
        // this to zero before snapshotting state.
        //
        // Synchronization is explicit at every call site: writes use Interlocked.Increment /
        // Interlocked.Decrement (full memory barriers) and reads use Volatile.Read. The
        // field is intentionally not declared volatile so the synchronization contract is
        // visible at each access rather than relying on a field-level annotation that a
        // future edit could silently drop.
        private int m_inFlightAppendLines;

        internal Encoding Encoding { get; }

        public SandboxedProcessOutputBuilder(
            Encoding encoding,
            int maxMemoryLength,
            ISandboxedProcessFileStorage fileStorage,
            SandboxedProcessFile file,
            Action<string> observer)
        {
            Contract.Requires(encoding != null);
            Contract.Requires(maxMemoryLength >= 0);

            HookOutputStream = (fileStorage != null || observer != null);

            m_stringBuilderWrapper = Pools.GetStringBuilder();
            m_stringBuilder = m_stringBuilderWrapper.Instance;

            Encoding = encoding;
            m_maxMemoryLength = maxMemoryLength;
            m_fileStorage = fileStorage;
            m_file = file;
            m_observer = observer;
        }

        private void ReleaseTextWriter()
        {
            var textWriter = m_textWriter;
            if (textWriter != null)
            {
                m_textWriter = null;
                HandleRecoverableIOException(textWriter.Dispose);
            }
        }

        private void ReleaseStringBuilder()
        {
            if (m_stringBuilder != null)
            {
                m_stringBuilder = null;
                m_stringBuilderWrapper.Dispose();
            }
        }

        [SuppressMessage("Microsoft.Usage", "CA2213:DisposableFieldsShouldBeDisposed", MessageId = "m_textWriter")]
        public void Dispose()
        {
            LatchAndDrain();
            ReleaseTextWriter();
            ReleaseStringBuilder();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public bool AppendLine(string data)
        {
            if (m_exception != null)
            {
                return true;
            }

            // Reserve a slot for this AppendLine call before checking the latch.
            // The Interlocked.Increment is a full memory barrier, so:
            //   - Either Freeze's Exchange(StateLatched) is observable here and we
            //     early-return without mutating, OR
            //   - Freeze observes our incremented counter and waits for our
            //     matching Decrement before snapshotting state.
            Interlocked.Increment(ref m_inFlightAppendLines);
            try
            {
                if (m_state != StateAccepting)
                {
                    // Builder has been latched by Freeze / Dispose. Drop this line
                    // rather than risk concurrent mutation of m_value / m_fileName.
                    // Under the production single-writer model this only fires on the
                    // teardown race; the dropped line is the trailing tail of a killed
                    // process whose output we're abandoning anyway.
                    return true;
                }

                if (data == null)
                {
                    ReleaseTextWriter();
                    // AppendLine(null) signals end-of-stream. Latch the state so any later
                    // AppendLine call early-returns at the state check rather than silently
                    // accumulating post-EOF data into the buffer.
                    Interlocked.Exchange(ref m_state, StateLatched);
                    return true;
                }

                m_observer?.Invoke(data);

                m_length += data.Length + Environment.NewLine.Length;
                if (m_textWriter != null)
                {
                    HandleRecoverableIOException(() => m_textWriter.WriteLine(data));
                }
                else if (m_fileStorage == null && m_stringBuilder.Length >= m_maxMemoryLength)
                {
                    // The caller should have configured an observer, called above. If not and no backing file configured,
                    // we start silently dropping the output stream at the in-memory buffer length.
                }
                else
                {
                    m_stringBuilder.AppendLine(data);
                    Contract.Assert(m_stringBuilder.Length == m_length);
                    if (m_length > m_maxMemoryLength && m_fileStorage != null)
                    {
                        HandleRecoverableIOException(
                            () =>
                            {
                                string fileName = m_fileStorage.GetFileName(m_file);
                                FileUtilities.CreateDirectory(Path.GetDirectoryName(fileName));

                                // Note that we use CreateReplacementFile since the target may be a read-only hardlink (e.g. in the build cache).
                                FileStream stream = FileUtilities.CreateReplacementFile(
                                    fileName,
                                    FileShare.Read | FileShare.Delete,
                                    openAsync: false);
                                m_textWriter = new StreamWriter(stream, Encoding);
                                m_textWriter.Write(m_stringBuilder.ToString());
                                ReleaseStringBuilder();

                                // Only assign m_fileName after all file operations succeed.
                                // Previously, m_fileName was set before entering this lambda, which meant
                                // that if an unexpected (non-IO) exception occurred during file creation,
                                // m_fileName would remain set while m_stringBuilder was still non-null,
                                // violating the XOR invariant in SandboxedProcessOutput's constructor.
                                m_fileName = fileName;
                            });
                    }
                }

                return true;
            }
            finally
            {
                Interlocked.Decrement(ref m_inFlightAppendLines);
            }
        }

        private void HandleRecoverableIOException(Action action)
        {
            try
            {
                ExceptionUtilities.HandleRecoverableIOException(
                    action,
                    ex => { throw new BuildXLException("Writing file failed", ex); });
            }
            catch (BuildXLException ex)
            {
                m_exception = ex;
                ReleaseTextWriter();
                ReleaseStringBuilder();
                m_fileName = null;
                m_length = SandboxedProcessOutput.NoLength;
                // The m_exception != null gate at the top of AppendLine prevents further
                // mutation after this catch returns, so no separate latch flip is needed.
            }
        }

        /// <summary>
        /// Whether the process wrapper should hook this stream, i.e. whether an observer is configured.
        /// When false, the stream output should be allowed to stream to the parent console.
        /// </summary>
        public bool HookOutputStream { get; }

        /// <summary>
        /// Obtain finalized output
        /// </summary>
        public SandboxedProcessOutput Freeze()
        {
            LatchAndDrain();
            ReleaseTextWriter();

            return new SandboxedProcessOutput(
                m_length,
                m_stringBuilder?.ToString(),
                m_fileName,
                Encoding,
                m_fileStorage,
                m_file,
                m_exception);
        }

        /// <summary>
        /// Latches the builder so that future <see cref="AppendLine"/> calls early-return
        /// without mutating, then waits for any in-flight <see cref="AppendLine"/> to
        /// complete. Idempotent — safe to call multiple times.
        /// </summary>
        private void LatchAndDrain()
        {
            // Flip the latch. We don't early-return on a second-Latched observation here
            // because AppendLine(null) (the EOF signal) also flips this latch from inside
            // its try block — i.e. while the calling AppendLine still holds an in-flight
            // slot. Always running the drain below ensures we wait for that AppendLine to
            // finish before snapshotting state, regardless of who flipped first. On the
            // common path (single Freeze / Dispose caller, no in-flight AppendLine) the
            // drain spin sees counter == 0 immediately and exits without spinning.
            Interlocked.Exchange(ref m_state, StateLatched);

            // Drain in-flight AppendLines. Under the production single-writer model
            // (one pipe-reader thread) this loop spins for at most a single AppendLine
            // body (no blocking IO except the spill path). SpinWait backs off to
            // Thread.Sleep after a few iterations, so this does not burn a core.
            // Volatile.Read prevents the JIT from caching the counter across iterations.
            var spin = new SpinWait();
            while (Volatile.Read(ref m_inFlightAppendLines) > 0)
            {
                spin.SpinOnce();
            }
        }
    }
}
