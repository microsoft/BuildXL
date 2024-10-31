// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tracing;

namespace BuildXL.App.Fuzzing;

internal class MockConsole : IConsole
{
    /// <inheritdoc/>
    public bool UpdatingConsole { get; set; } = false;

    /// <inheritdoc/>
    public IntPtr ConsoleWindowHandle => IntPtr.Zero;

    /// <inheritdoc/>
    public void Dispose() { }

    /// <inheritdoc/>
    public void WriteHyperlink(MessageLevel messageLevel, string text, string target) { }
    
    /// <inheritdoc/>
    public void WriteOutput(MessageLevel messageLevel, string text) { }
    
    /// <inheritdoc/>
    public void WriteOutputLine(MessageLevel messageLevel, string line) { }
    
    /// <inheritdoc/>
    public void WriteOverwritableOutputLine(MessageLevel messageLevel, string standardLine, string overwritableLine) { }
    
    /// <inheritdoc/>
    public void WriteOverwritableOutputLineOnlyIfSupported(MessageLevel messageLevel, string standardLine, string overwritableLine) { }

    /// <inheritdoc/>
    public void ReportProgress(ulong done, ulong total) { }

    /// <inheritdoc/>
    public void SetRecoverableErrorAction(Action<Exception> errorAction) { }
}

/// <summary>
/// Fuzzable code that is called by the OneFuzz fuzzer.
/// </summary>
/// <remarks>
/// OneFuzz documentation for dotnet code can be found here: https://eng.ms/docs/cloud-ai-platform/azure-edge-platform-aep/aep-security/epsf-edge-and-platform-security-fundamentals/the-onefuzz-service/onefuzz/howto/fuzzing-dotnet-code
/// </remarks>
public static class FuzzableCode
{
    /// <nodoc/>
    public static void FuzzTargetMethod(ReadOnlySpan<byte> input)
    {
        var inputStr = Encoding.ASCII.GetString(input);
        const int chunk = 100;
        var args = Enumerable.Range(0, inputStr.Length / chunk).Select(i => "/" + inputStr.Substring(i * chunk, chunk));

        using (var argsParser = new Args(new MockConsole()))
        {
            // No unhandled exception or undefined behavior is expected. This graceful termination will be checked by the 'outer' system that does the fuzzy checking.
            argsParser.TryParse(args.ToArray(), new PathTable(), out var arguments);
        }
    }
}