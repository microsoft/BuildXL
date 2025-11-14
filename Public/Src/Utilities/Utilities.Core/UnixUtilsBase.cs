// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// Cache of results from <see cref="CheckConditionAgainstStandardOutput(string, string, Func{string, bool}, out string, bool, bool, int, Action{int, string})"/>.
    /// </summary>
    private readonly ConcurrentDictionary<string, (bool result, string error)> m_cache = new();

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
    protected bool CheckConditionAgainstStandardOutput(
        string binaryPath, 
        string arguments, 
        Func<string, bool> condition, 
        out string standardError, 
        bool runAsSudo = false, 
        bool interactive = false, 
        int retries = 1, 
        Action<int, string> retryAction = null)
    {
        if (!OperatingSystemHelper.IsLinuxOS || !m_isToolInstalled || !File.Exists(binaryPath))
        {
            standardError = !OperatingSystemHelper.IsLinuxOS ? "Not a Linux OS" : !m_isToolInstalled ? $"{m_toolPath} not found" : $"File {binaryPath} not found";
            return false;
        }

        // It is possible that this path could have the same timestamp if deployed by cache
        // TODO [pgunasekara]: Revisit this if this case is happening in production queues and use a file hash for the key instead
        var pathKey = $"{File.GetLastWriteTime(binaryPath)}|{binaryPath}";

        if (m_cache.TryGetValue(pathKey, out var result))
        {
            standardError = result.error;
            return result.result;
        }

        string filename;
        if (runAsSudo)
        {
            filename = "/usr/bin/sudo";
            arguments = $"{m_toolPath} {arguments}";
            if (!interactive)
            {
                // If not interactive, we don't want sudo to prompt for a password
                arguments = "-n " + arguments;
            }

        }
        else
        {
            filename = m_toolPath;
        }

        var processInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = filename,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // If interactive is true, we want to show the window so that user can enter password for sudo
            WindowStyle = runAsSudo && interactive ? System.Diagnostics.ProcessWindowStyle.Normal : System.Diagnostics.ProcessWindowStyle.Hidden,
            UseShellExecute = false,
            ErrorDialog = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        int exitCode;
        string standardOutput;
        do {
            exitCode = RunProcessWithTimeout(processInfo, interactive, out standardError, out standardOutput);
            retries--;
            if (exitCode != 0 && retries > 0 && retryAction != null)
            {
                retryAction(retries, standardError);
            }
        } while (exitCode != 0 && retries > 0);

        // If the process ran successfully, check the condition
        if (exitCode == 0)
        {
            var check = condition(standardOutput);
            m_cache.TryAdd(pathKey, (check, standardError));

            return check;
        }

        m_cache.TryAdd(pathKey, (false, standardError));

        return false;
    }

    private static void Kill(System.Diagnostics.Process p)
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

    /// <summary>
    /// Determines if sudo will require an interactive password prompt
    /// </summary>
    protected static bool WillSudoPromptForPassword()
    {
        if (!OperatingSystemHelper.IsLinuxOS)
        {
            return false;
        }

        // Check if sudo exists
        if (!File.Exists("/usr/bin/sudo"))
        {
            return false;
        }

        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/usr/bin/sudo",
                Arguments = "-n true", // Non-interactive test command
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
                UseShellExecute = false,
                ErrorDialog = false,
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null)
            {
                return true; // Assume prompt required if we can't test
            }

            // Wait up to 2 seconds for the test
            if (!process.WaitForExit(2000))
            {
                process.Kill();
                return true; // Assume prompt required if test hangs
            }

            process.WaitForExit();

            // Exit code 0 means sudo worked without password, so no prompt required
            return process.ExitCode != 0;
        }
        catch (Exception)
        {
            // If we can't determine, assume prompt is required for safety
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            return true;
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
        }
    }

    private static int RunProcessWithTimeout(ProcessStartInfo processInfo, bool interactive, out string standardError, out string standardOutput)
    {
        using var process = System.Diagnostics.Process.Start(processInfo);
        if (process == null)
        {
            standardError = "Failed to start process";
            standardOutput = string.Empty;
            return -1;
        }

        try
        {
#pragma warning disable AsyncFixer02 // WaitForExitAsync should be used instead
            // Only waiting 2 (or 30 if interactive) seconds at the most, although it should never take this long
            int timeoutSecs = interactive ? 30 : 2;
            bool exited = process.WaitForExit(timeoutSecs * 1000);
            if (!exited)
            {
                Kill(process);
                standardError = $"Process timed out after {timeoutSecs} seconds";
                standardOutput = string.Empty;

                return -1;
            }

            process.WaitForExit();
            standardOutput = process.StandardOutput.ReadToEnd();
            standardError = process.StandardError.ReadToEnd();

            return process.ExitCode;
#pragma warning restore AsyncFixer02
        }
#pragma warning disable EPC12 // An exit point '}' swallows an unobserved exception.
        catch (Exception e)
        {
            // Best effort
            standardError = e.Message;
            standardOutput = string.Empty;
            
            return -1;
        }
#pragma warning restore EPC12
    }
}
