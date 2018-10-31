// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Utilities.Tracing
{
    /// <summary>
    /// Message level for WriteOutputLine
    /// </summary>
    public enum MessageLevel : byte
    {
        /// <summary>
        /// Info messaage. Usually shown in the default console color.
        /// </summary>
        Info = 0,

        /// <summary>
        /// Warning message. Usually shown in yellow.
        /// </summary>
        Warning = 1,

        /// <summary>
        /// Error message. Usually shown in red
        /// </summary>
        Error = 2,

        /// <summary>
        /// Error message that is shown in the default console color but goes to StdErr stream.
        /// </summary>
        ErrorNoColor = 3,
    }

    /// <summary>
    /// Write-only console-like event consumer. Accepts line-buffered text.
    /// Each write operation is thread-safe and atomic (text does not intermingle).
    /// It also can control a progress bar.
    /// </summary>
    public interface IConsole : IDisposable
    {
        /// <summary>
        /// Writes a line with the given message level.
        /// </summary>
        void WriteOutputLine(MessageLevel messageLevel, string line);

        /// <summary>
        /// Writes an overwritable output line. Overwritable lines can get overwritten by subsequent messages.
        /// </summary>
        void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardLine, string overwritableLine);

        /// <summary>
        /// Writes an overwritable output line only if overwriting is supported by the console. Overwritable lines can get overwritten by subsequent messages.
        /// </summary>
        void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine);

        /// <summary>
        /// Report the progress done
        /// </summary>
        /// <param name="done">The number done items</param>
        /// <param name="total">The total number of items</param>
        void ReportProgress(ulong done, ulong total);
    }
}
