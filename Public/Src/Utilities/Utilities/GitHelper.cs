// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using BuildXL.Utilities.Core;
using System.Threading.Tasks;
using System.Text;
using System.Diagnostics.ContractsLight;
using System.Linq;

#nullable enable
namespace BuildXL
{
    internal sealed class GitHelperFailure : Failure
    {
        private readonly string m_description;
        private readonly Exception? m_ex;

        public GitHelperFailure(string description, Exception? ex = null)
        {
            m_description = description;
            m_ex = ex;
        }

        public override BuildXLException CreateException()
        {
            return new BuildXLException(Describe());
        }

        /// <inheritdoc/>
        public override string Describe()
        {
            return $"GitHelper failure: {m_description}." + (m_ex != null ? $"Inner exception details: {m_ex.Message}" : string.Empty);
        }

        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }

    /// <summary>
    /// An object that can retrieve information from git.
    /// they return failures otherwise.
    /// </summary>
    /// <remarks>
    /// Abstracted for the sake of testing
    /// </remarks>
    public interface IGitHelper
    {
        /// <summary>
        /// Returns the merge-base between two commits or refs
        /// See https://git-scm.com/docs/git-merge-base
        /// </summary>
        Task<Possible<string>> GetMergeBaseAsync(string a, string b);

        /// <summary>
        /// Returns a list of the latest {count} commit hashes starting from the specified ref,
        /// in reverse chronological order (as a regular `git log` would output)
        /// </summary>
        Task<Possible<IReadOnlyList<string>>> GetLatestCommitHashesAsync(int count, string refName = "HEAD");
    }

    /// <summary>
    /// An <see cref="IGitHelper"/> that invokes git by launching an external process to run the corresponding commands.
    /// The operations work only if git is installed the system the program is running inside a git repository
    /// </summary>
    public class GitHelper : IGitHelper
    {
        private static readonly TimeSpan s_operationTimeout = TimeSpan.FromSeconds(5);

        private readonly string? m_gitLocation;
        private readonly string? m_repositoryPath;

        /// <summary>
        /// Creates a GitHeper that will run git from the specified executable path
        /// and on the specified directory as working directory. 
        /// </summary>
        /// <param name="gitLocation">Git executable location. If unspecified, it is assumed that 'git' is in the PATH</param>
        /// <param name="repositoryPath">Working directory where git will be run. If unspecified, the working directory is used.</param>
        public GitHelper(string? gitLocation = null, string? repositoryPath = null)
        {
            m_gitLocation = gitLocation;
            m_repositoryPath = repositoryPath;
        }

        /// <inheritdoc />
        public async Task<Possible<string>> GetMergeBaseAsync(string a, string b)
        {
            return (await ExecuteGitCommandAsync($"merge-base {a} {b}")).Then(lines => lines.Single());
        }

        /// <inheritdoc />
        public async Task<Possible<IReadOnlyList<string>>> GetLatestCommitHashesAsync(int count, string refName = "HEAD")
        {
            Contract.Requires(!string.IsNullOrEmpty(refName));
            return (await ExecuteGitCommandAsync($"log --pretty=format:%H -n {count} {refName}")).Then(hashes => hashes);
        }

        internal async Task<Possible<IReadOnlyList<string>>> ExecuteGitCommandAsync(string gitArguments, bool expectStdOut = true)
        {
            var process = new Process();

            process.StartInfo.FileName = m_gitLocation ?? "git";
            if (m_repositoryPath is not null)
            {
                process.StartInfo.WorkingDirectory = m_repositoryPath;
            }
            process.StartInfo.Arguments = gitArguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.EnableRaisingEvents = true;

            List<string> lines = new();
            StringBuilder errorBuilder = new();

            var executor = new AsyncProcessExecutor(process, s_operationTimeout,
                outputBuilder: line => { if (!string.IsNullOrEmpty(line)) { lines.Add(line); } },
                errorBuilder: line => { if (!string.IsNullOrEmpty(line)) { errorBuilder.AppendLine(line); } });

            try
            {
                executor.Start();
            }
            catch (Exception e)
            {
                return new GitHelperFailure($"Failed to start git. Details: {e}");
            }

            await executor.WaitForExitAsync();
            await executor.WaitForStdOutAndStdErrAsync();

            if (executor.TimedOut)
            {
                return new GitHelperFailure($"Timed out while performing git command '{gitArguments}'. Timeout: {s_operationTimeout.TotalSeconds} seconds");
            }
            else if (executor.Process.ExitCode != 0)
            {
                return new GitHelperFailure($"git command '{gitArguments}' failed with exit code {executor.Process.ExitCode}. StdErr:\n{errorBuilder}");
            }
            else if (expectStdOut && lines.Count == 0)
            {
                return new GitHelperFailure($"'git {gitArguments}' succeeded but did not produce any standard output. This is unexpected.");
            }

            return lines;
        }
    }
}
