// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;

namespace BuildXL.Utilities.Core;

/// <summary>
/// Base class that executes a given Unix tool and checks its standard output against a given condition. The result is cached.
/// </summary>
public abstract class UnixUtilsBase
{
    private readonly string m_toolPath;
    private readonly bool m_isToolInstalled;

    /// <summary>
    /// Cache of results from <see cref="CheckConditionAgainstStandardOutput(string, string, Func{string, bool})"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> m_cache = new();

    /// <nodoc/>
    protected UnixUtilsBase(string toolPath)
    {
        Contract.Assert(!string.IsNullOrEmpty(toolPath));
        m_toolPath = toolPath;
        m_isToolInstalled = OperatingSystemHelper.IsLinuxOS && File.Exists(m_toolPath);
    }

    /// <summary>
    /// Returns true if the execution of the given binary with the provided arguments produces a standard output that matches the given condition
    /// </summary>
    /// <remarks>
    /// The provided cache is used for retrieval/storage
    /// </remarks>
    protected bool CheckConditionAgainstStandardOutput(string binaryPath, string arguments, Func<string, bool> condition)
    {
        if (!OperatingSystemHelper.IsLinuxOS || !m_isToolInstalled || !File.Exists(binaryPath))
        {
            return false;
        }

        // It is possible that this path could have the same timestamp if deployed by cache
        // TODO [pgunasekara]: Revisit this if this case is happening in production queues and use a file hash for the key instead
        var pathKey = $"{File.GetLastWriteTime(binaryPath)}|{binaryPath}";

        if (m_cache.TryGetValue(pathKey, out var result))
        {
            return result;
        }

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = m_toolPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            ErrorDialog = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            return false;
        }

        string stdout = string.Empty;

        try
        {
            stdout = process.StandardOutput.ReadToEnd();
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
            if (!process.WaitForExit(2 * 1000))
            {
                // Only waiting 2 seconds at the most, although it should never take this long
                kill(process);
                m_cache.TryAdd(pathKey, false);
            }
            process.WaitForExit();
#pragma warning restore AsyncFixer02
        }
#pragma warning disable ERP022 // An exit point '}' swallows an unobserved exception.
        catch (Exception)
        {
            // Best effort
            m_cache.TryAdd(pathKey, false);
        }
#pragma warning restore ERP022

        if (process.ExitCode == 0)
        {
            var check = condition(stdout);
            m_cache.TryAdd(pathKey, check);

            return check;
        }

        static void kill(System.Diagnostics.Process p)
        {
            if (p == null || p.HasExited)
            {
                return;
            }

            try
            {
                p.Kill();
            }
            catch (InvalidOperationException)
            {
                // the process may have exited,
                // in this case ignore the exception
            }
        }

        return false;
    }
}
