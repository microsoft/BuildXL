// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Implementation of <see cref="IConsole" /> targetting the standard console handles (stdout, stderr).
    /// </summary>
    public sealed class StandardConsole : IConsole
    {
        private readonly object m_lock = new object();
        private TaskbarInterop m_taskbar;
        private readonly ConsoleColor m_defaultForegroundColor;
        private readonly ConsoleColor m_errorColor;
        private readonly ConsoleColor m_warningColor;
        private MessageLevel m_highestMessageLevel;
        private bool m_isDisposed;
        private readonly bool m_updatingConsole;
        private int m_firstLineLength;
        private string m_lastOverwriteableLine;
        private readonly PathTranslator m_pathTranslator;
        private readonly bool m_debugConsole;

        /// <summary>
        /// Create a new standard console
        /// </summary>
        /// <param name="colorize">
        /// When true, errors and warnings are colorized in the output. When false, all output is
        /// monochrome.
        /// </param>
        /// <param name="animateTaskbar">
        /// When true, BuildXL animates its taskbar icon during execution.
        /// </param>
        /// <param name="supportsOverwriting">
        /// When true, messages printed to the console may be updated in-place (if supported)
        /// </param>
        /// <param name="pathTranslator">
        /// The object to use to translate paths from one root to another (if specified)
        /// </param>
        public StandardConsole(bool colorize, bool animateTaskbar, bool supportsOverwriting, PathTranslator pathTranslator = null)
        {
            m_defaultForegroundColor = Console.ForegroundColor;

            // If any output is redirected, updating the cursor position won't work. We assume the output won't get
            // redirected after the process starts.
            m_updatingConsole = !Console.IsOutputRedirected && !Console.IsErrorRedirected && supportsOverwriting;
            m_pathTranslator = pathTranslator;

            if (colorize)
            {
                m_errorColor = ConsoleColor.Red;
                m_warningColor = ConsoleColor.Yellow;
            }
            else
            {
                m_errorColor = m_warningColor = m_defaultForegroundColor;
            }

            if (animateTaskbar)
            {
                m_taskbar = new TaskbarInterop();
                m_taskbar.Init();
                m_taskbar.SetTaskbarState(TaskbarInterop.TaskbarProgressStates.Normal);
            }

            var debugConsole = Environment.GetEnvironmentVariable("BUILDXLDEBUGCONSOLE");
            if (!string.IsNullOrWhiteSpace(debugConsole) && debugConsole != "0")
            {
                m_debugConsole = true;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (m_lock)
            {
                if (!m_isDisposed)
                {
                    if (m_taskbar != null)
                    {
                        m_taskbar.SetTaskbarState(TaskbarInterop.TaskbarProgressStates.NoProgress);
                        m_taskbar.Dispose();
                        m_taskbar = null;
                    }
                }

                m_isDisposed = true;
            }
        }

        /// <inheritdoc />
        public void WriteOutputLine(MessageLevel messageLevel, string line)
        {
            WriteOutputLine(messageLevel, line, overwritable: false);
        }

        /// <inheritdoc />
        public void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardLine, string overwritableLine)
        {
            WriteOutputLine(messageLevel, m_updatingConsole ? overwritableLine : standardLine, overwritable: m_updatingConsole);
        }

        /// <inheritdoc />
        public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine)
        {
            if (m_updatingConsole)
            {
                WriteOutputLine(messageLevel, m_updatingConsole ? overwritableLine : standardLine,
                    overwritable: m_updatingConsole);
            }
        }

        public static int GetConsoleWidth()
        {
            // Not all consoles have a width defined, therefore return 150 which is a reasonable default on Developer boxes.
            int defaultWidth = 150;

            try
            {
                // https://github.com/dotnet/corefx/issues/4636
                int width = BuildXL.Utilities.OperatingSystemHelper.IsUnixOS ? Math.Max(defaultWidth, Console.WindowWidth) : Console.BufferWidth;

                return width;
            }
            catch (IOException)
            {
                return defaultWidth;
            }
        }

        private void WriteOutputLine(MessageLevel messageLevel, string line, bool overwritable)
        {
            TextWriter writer;
            ConsoleColor color;

            if (m_pathTranslator != null)
            {
                line = m_pathTranslator.Translate(line);
            }

            switch (messageLevel)
            {
                case MessageLevel.Info:
                    writer = Console.Out;
                    color = m_defaultForegroundColor;
                    break;
                case MessageLevel.Warning:
                    writer = Console.Out;
                    color = m_warningColor;
                    break;
                case MessageLevel.Error:
                    writer = Console.Error;
                    color = m_errorColor;
                    break;
                default:
                    Contract.Assert(messageLevel == MessageLevel.ErrorNoColor);
                    writer = Console.Error;
                    color = m_defaultForegroundColor;
                    break;
            }

            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }

                if (m_taskbar != null && m_highestMessageLevel < messageLevel)
                {
                    m_highestMessageLevel = messageLevel;

                    switch (m_highestMessageLevel)
                    {
                        case MessageLevel.Info:
                            m_taskbar.SetTaskbarState(TaskbarInterop.TaskbarProgressStates.Normal);
                            break;
                        case MessageLevel.Warning:
                            m_taskbar.SetTaskbarState(TaskbarInterop.TaskbarProgressStates.Paused);
                            break;
                        default:
                            Contract.Assert(m_highestMessageLevel == MessageLevel.Error || m_highestMessageLevel == MessageLevel.ErrorNoColor);
                            m_taskbar.SetTaskbarState(TaskbarInterop.TaskbarProgressStates.Error);
                            break;
                    }
                }

                try
                {
                    if (color != m_defaultForegroundColor)
                    {
                        Console.ForegroundColor = color;
                    }

                    // Details on handling overwriteable lines:
                    // Let's let X represent the cursor's position and - represent blank characters.
                    //
                    // The general principle is that overwriteable lines reset the position of the cursor to where it
                    // was before that line was written.
                    // This is the state after writing a string with a newline in it. Notice the first line has
                    // already wrapped because it was wider than the console
                    // _______________________________
                    // |X ThisLineIsPrettyLongAndMayWr|
                    // |apAroundToTheNextLine---------|
                    // |MoreText----------------------|
                    // _______________________________
                    //
                    // Then before a subsequent message is written, we compute a blank string long enough to overwrite all of
                    // the text that needs to be overwritten. That string is written to the console, and the cursor is reset
                    // a second time to be ready for a new message.
                    //
                    // The state of the console is now this, which is perfect for writing the next line.
                    //
                    // |X-----------------------------|
                    // |------------------------------|
                    // |------------------------------|
                    // _______________________________
                    //
                    // But...
                    // The big complication is the window can be resized before the blanking message is written.
                    // When the window starts resizing, the cursor is moved to the end of the line is currently on. All
                    // text after the cursor is truncated. All text before the cursor is wrapped as the window is resized.
                    // Take the first example from above again. When the window starts to resize, the cursor gets moved
                    // to the 'r' in Wrap. After resizing, the state of the console could now be this
                    //
                    // __________________
                    // |ThisLineIsPretty|
                    // |LongAndMayWX----|
                    // _________________
                    //
                    // Notice the cursor is no longer at the beginning of the text we want to overwrite. In order to
                    // account for this, we must track the width of the first line and move the cursor back again if
                    // the window size is smaller than it was when the first line was written.
                    if (m_lastOverwriteableLine != null)
                    {
                        int bufferWidth = GetConsoleWidth();

                        // An overwriteable line was previously written. 3 operations must be performed:

                        // 1. Check to see if the cursor's position needs to be adjusted in case the window was resized
                        int linesToMoveCursorUp = 0;
                        while (m_firstLineLength > bufferWidth)
                        {
                            linesToMoveCursorUp++;
                            m_firstLineLength -= bufferWidth;
                        }

                        if (linesToMoveCursorUp > 0)
                        {
                            Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - linesToMoveCursorUp));
                        }

                        // 2. Write enough blank text to completely overwrite the previous text
                        int backtrackLines = ComputeLinesUsedByString(m_lastOverwriteableLine, bufferWidth);
                        Console.Write(new string(' ', backtrackLines * bufferWidth));

                        // The console behavior is subtly different in newer Redstone builds. Previously, if the buffer
                        // was 120 characters and 120 characters were written, the cursor would move itself down to the
                        // next line. Now the cursor may actually stay at column position 120 on the original line and
                        // not move down to the next until the 121st character is written. We expect the CursorLeft to be
                        // at the 0 position after writing out the blanking text. So if we detect it isn't, this must be
                        // an OS that has the different behavior. So we manually move the cursor down by writing an extra
                        // character. This ensures the SetCursorPosition call below resets the cursor to the correct place
                        //
                        // This would all be much easier if we could just directly measure how many lines the cursor moved
                        // after writing the blanking line. But if we are at the end of the console buffer, the CursorTop
                        // will just remain constant.
                        if (Console.CursorLeft != 0)
                        {
                            Console.Write(' ');
                        }

                        // 3. Reset the cursor back to its place before #2 so we can write new text in the same place.
                        Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - backtrackLines));
                        m_lastOverwriteableLine = null;
                        m_firstLineLength = 0;
                    }

                    if (overwritable)
                    {
                        int original = 0;
                        int current = 0;
                        if (m_debugConsole)
                        {
                            original = Console.CursorTop;
                        }

                        writer.WriteLine(line);

                        if (m_debugConsole)
                        {
                            current = Console.CursorTop;
                        }

                        // After writing an overwriteable line, we must capture some information so the next message can
                        // overwrite it
                        m_lastOverwriteableLine = line;

                        int bufferWidth = GetConsoleWidth();

                        m_firstLineLength = Math.Min(GetFirstLineLength(line), bufferWidth);

                        // Now reset the cursor position to the beginning of the overwriteable line
                        int computed = ComputeLinesUsedByString(line, bufferWidth);
                        Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - computed));

                        if (m_debugConsole)
                        {
                            /*
                             * The code to compute the number of lines written is fickle and can break if Windows changes
                             * how the console behaves. Uncommenting the block to print these internal calculations vs. the
                             * actual is helpful for debugging issues. */
                            int actual = current - original;
                            Console.Write(computed + ":" + actual + "   ");
                            Console.SetCursorPosition(0, Console.CursorTop);
                        }
                    }
                    else
                    {
                        writer.WriteLine(line);
                    }

                    if (color != m_defaultForegroundColor)
                    {
                        Console.ForegroundColor = m_defaultForegroundColor;
                    }
                }
                catch (IOException ex)
                {
                    // We know that the problem is in the console. No need to guess by calling AnalyzeExceptionRootCause
                    throw new BuildXLException("IOException caught in " + nameof(WriteOutputLine), ex, ExceptionRootCause.ConsoleNotConnected);
                }
            }
        }

        internal static int GetFirstLineLength(string line)
        {
            int newlinePosition = line.IndexOf(Environment.NewLine, StringComparison.OrdinalIgnoreCase);
            int firstLineLength = newlinePosition == -1 ? line.Length : newlinePosition;

            return firstLineLength;
        }

        internal static int ComputeLinesUsedByString(string line, int consoleWidth)
        {
            string[] splitLines = line.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            int lineCount = 0;
            foreach (var split in splitLines)
            {
                lineCount++;

                int lineLength = split.Length;
                while (lineLength > consoleWidth)
                {
                    lineCount++;
                    lineLength -= consoleWidth;
                }
            }

            return lineCount;
        }

        /// <inheritdoc />
        public void ReportProgress(ulong done, ulong total)
        {
            if (m_taskbar == null)
            {
                return;
            }

            lock (m_lock)
            {
                if (m_isDisposed)
                {
                    return;
                }

                m_taskbar.SetProgressValue(done, total);
            }
        }
    }
}
