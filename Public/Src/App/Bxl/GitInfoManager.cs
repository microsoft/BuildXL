using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.App.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

#nullable enable

namespace BuildXL
{
    /// <summary>
    /// This class aims to collect the remote repo URL from a build.
    /// </summary>
    /// <remarks>
    /// The current approach involves finding and parsing of the Git config file, .git/config, to extract the remote URL.
    /// This is just a best-effort approach and may not work in all cases, particularly when the .git/config file is not in the expected format, which causes parsing errors.
    /// In the future, we may consider other approaches to get the remote repo URL, such as spawning an external Git process like `git.exe config --get remote.origin.url`.
    /// However this approach is not preferred because it requires knowing the path to git.exe or requires the git.exe to be in the path env var, which may not always the case.
    /// As per the triangular fork convention, the remote origin URL denotes the fork repo, while the conventionally-named "upstream" URL points to the original repo
    /// from which the fork originated. In the below sample of .git/config file, the expected remote URL will be https://dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream.
    /// [core]
    ///     repositoryformatversion = 0
    ///     filemode = false
    ///     bare = false
    ///     logallrefupdates = true
    ///     symlinks = false
    ///     ignorecase = true
    /// [remote "origin"]
    ///     url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
    ///     fetch = +refs/heads/*:refs/remotes/origin/*
    /// [remote "upstream"]
    ///     url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
    ///     fetch = +refs/heads/*:refs/remotes/upstream/*
    /// [branch "master"]
    ///     remote = origin
    ///     merge = refs/heads/master
    /// </remarks>
    public class GitInfoManager
    {
        private static readonly Regex s_remoteSectionRegex = new(@"^\[\s*(?i)remote(?-i)\s*""\s*(.*?)""\s*\]");
        private static readonly Regex s_remoteUrlRegex = new(@"^\s*(?i)url(?-i)\s*=\s*(.*)$");

        private readonly string m_startDirectory;
        private readonly string? m_endDirectory;

        private GitInfoManager(string startDirectory, string? endDirectory)
        {
            m_startDirectory = startDirectory;
            m_endDirectory = endDirectory;
        }

        /// <summary>
        /// Creates an instance of <see cref="GitInfoManager"/>.
        /// </summary>
        /// <param name="startDirectory">Starting directory for finding Git config file.</param>
        /// <param name="endDirectory">End directory for finding Git config file.</param>
        public static GitInfoManager Create(string startDirectory, string? endDirectory = default)
        {
            startDirectory = normalizePath(startDirectory);
            endDirectory = endDirectory == null ? endDirectory : normalizePath(endDirectory);

            if (endDirectory != null && !startDirectory.StartsWith(endDirectory + Path.DirectorySeparatorChar, OperatingSystemHelper.PathComparison))
            {
                throw new ArgumentException($"Start directory '{startDirectory}' is not a prefix path of end directory '{endDirectory}'.");
            }

            return new GitInfoManager(startDirectory, endDirectory);

            static string normalizePath(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        }

        /// <summary>
        /// Gets the remote repo URL.
        /// </summary>
        public Possible<(string gitRemoteRepoUrl, string gitConfigFileName)> GetRemoteRepoUrl()
        {
            const string FailurePrefix = "Failed to get Git remote repo URL because";
            DirectoryInfo? gitDirectory = GetLocalRepoRoot();
            if (gitDirectory == null)
            {
                return new Failure<string>($"{FailurePrefix} .git folder is not found after searching from '{m_startDirectory}'");
            }

            FileInfo? gitConfigFile = gitDirectory.GetFiles("config").FirstOrDefault();
            if (gitConfigFile == null)
            {
                return new Failure<string>($"{FailurePrefix} Git config file is not found in '{gitDirectory.FullName}'");
            }

            Possible<string?> maybeRemoteUrl = ParseGitConfigContent(gitConfigFile);

            if (!maybeRemoteUrl.Succeeded)
            {
                return new Failure<string>($"{FailurePrefix} parsing '{gitConfigFile.FullName}' resulted in failure: {maybeRemoteUrl.Failure.DescribeIncludingInnerFailures()}");
            }

            string? remoteUrl = maybeRemoteUrl.Result;
            if (remoteUrl == null)
            {
                return new Failure<string>($"{FailurePrefix} parsing '{gitConfigFile.FullName}' did not find remote repo URL");
            }

            return (gitRemoteRepoUrl: remoteUrl, gitConfigFileName: gitConfigFile.FullName);
        }

        internal static string? ParseGitConfigContent(string gitConfigFileContent)
        {
            using var reader = new StringReader(gitConfigFileContent);
            return ParseGitConfigContent(reader);
        }

        private static Possible<string?> ParseGitConfigContent(FileInfo file)
        {
            try
            {
                using var reader = new StreamReader(file.OpenRead());
                return ParseGitConfigContent(reader);
            }
            catch (IOException e)
            {
                return new Failure<string>(e.Message);
            }
        }

        private static string? ParseGitConfigContent(TextReader reader)
        {
            string? line;
            string? currentRemoteName = null;
            string? foundLastOriginUrl = null;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.StartsWith('['))
                {
                    // Section start.

                    // Match with `[remote "<remote name>"]`.
                    var match = s_remoteSectionRegex.Match(line);
                    currentRemoteName = match.Success ? match.Groups[1].Value.Trim() : null;
                }
                else if (currentRemoteName != null)
                {
                    // Match with `url = <remote url>`.
                    var matchUrl = s_remoteUrlRegex.Match(line);
                    if (matchUrl.Success)
                    {
                        // Since a remote named "upstream" has the highest precendece,
                        // stop parsing the file and return its associated URL.
                        if (string.Equals(currentRemoteName, "upstream", StringComparison.OrdinalIgnoreCase))
                        {
                            return StripCredential(matchUrl.Groups[1].Value.Trim());
                        }

                        // Keep track of the last "origin" remote URL.
                        if (string.Equals(currentRemoteName, "origin", StringComparison.OrdinalIgnoreCase))
                        {
                            foundLastOriginUrl = matchUrl.Groups[1].Value.Trim();
                        }
                    }
                }
                else
                {
                    currentRemoteName = null;
                }
            }

            return foundLastOriginUrl == null ? null : StripCredential(foundLastOriginUrl);
        }

        private DirectoryInfo? GetLocalRepoRoot()
        {
            var currentDirectory = new DirectoryInfo(m_startDirectory);

            while (currentDirectory != null && (m_endDirectory == null || !string.Equals(currentDirectory.FullName, m_endDirectory, OperatingSystemHelper.PathComparison)))
            {
                if (currentDirectory.Exists)
                {
                    var gitDirectory = currentDirectory.GetDirectories(".git").FirstOrDefault();

                    if (gitDirectory != null)
                    {
                        return gitDirectory;
                    }
                }

                currentDirectory = currentDirectory.Parent;
            }

            return null;
        }

        private static string StripCredential(string remoteUri)
        {
            if (Uri.TryCreate(remoteUri, UriKind.Absolute, out Uri? uri))
            {
                return new UriBuilder(uri) { Password = null, UserName = null }.Uri.ToString();
            }

            // If URL is not of valid form (it could be a local file path or an SSH URL)
            // there is no reasonable expectation for it to contain passwords/PATs
            return remoteUri;
        }
    }
}
