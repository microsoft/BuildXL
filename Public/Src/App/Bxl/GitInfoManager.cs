using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Utilities.Instrumentation.Common;
using TracingLog = BuildXL.App.Tracing;

namespace BuildXL
{
    /// <summary>
    /// This class aims to collect the remote repo Url from a build. 
    /// </summary>
    /// <remarks>
    /// The current approach involves parsing of the .git/config file for the required remote Url.
    /// If there are numerous parsing errors or issues in detecting the .git/config file using this method, in the future we may consider another approach where we spawn an external git.exe process to get needed git info.
    /// With the other approach we will need to apply string manipulations on the path env var for finding the git.exe file path, which involves more or less the same effort as the other stratergy.
    /// Note that, in the sample.git/config below, the remote origin Url denotes the fork repo, while the conventionally-named "upstream" Url points to the original repo from which the fork originated.
    /// Sample gitconfig file
    /// [core]
    /// repositoryformatversion = 0
    /// filemode = false
    /// bare = false
    /// logallrefupdates = true
    /// symlinks = false
    /// ignorecase = true
    /// [remote "origin"]
    /// url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalOrigin
    /// fetch = +refs/heads/*:refs/remotes/origin/*
    /// [remote "upstream"]
    /// url = https://mseng@dev.azure.com/mseng/Domino/_git/BuildXL.InternalUpstream
    /// fetch = +refs/heads/*:refs/remotes/upstream/*
    /// [branch "master"]
    /// remote = origin
    /// merge = refs/heads/master
    /// </remarks>
    public class GitInfoManager
    {
        private readonly LoggingContext m_loggingContext;
        private readonly string m_startDirectory;

        // In most of the cases the current directory is the root of the git repository.
        private const string GitConfigRelativeFilePath = @"\.git\config";

        // Matches the remote section of the git config file.
        private static Regex s_remoteSectionRegex = new Regex(@"^\[\s*remote\s*""\s*(.*?)""\s*\]");

        // Matches the Url field under the remote section.
        private static Regex s_remoteUrlRegex = new Regex(@"^\s*Url\s*=\s*(.*)$");

        /// <nodoc />
        public GitInfoManager(LoggingContext loggingContext, string startDirectory)
        {
            m_loggingContext = loggingContext;
            m_startDirectory = Path.GetFullPath(startDirectory);
        }

        /// <summary>
        /// Find the location of the git config file and capture git remote repo information.
        /// </summary>
        public string GetRemoteRepoInfo()
        {
            string gitConfigFilePath = null;
            var currentDirectory = new DirectoryInfo(m_startDirectory);

            // Usually, the current directory is the root of the git repository.If it's not, we search the parent directories for the file.
            while (currentDirectory != null)
            {
                if (currentDirectory.Exists)
                {
                    gitConfigFilePath = Path.Combine(currentDirectory.FullName, GitConfigRelativeFilePath);
                    if (File.Exists(gitConfigFilePath))
                    {
                        break;
                    }
                }

                currentDirectory = currentDirectory.Parent;
            }

            if (currentDirectory != null)
            {
                Contract.Assert(!string.IsNullOrEmpty(gitConfigFilePath));
                TracingLog.Logger.Log.FoundGitConfigFile(m_loggingContext, gitConfigFilePath);
                return GetGitRemoteUrl(gitConfigFilePath);
            }

            TracingLog.Logger.Log.FailedToCaptureGitRemoteRepoInfo(m_loggingContext, $"Git config file is not found after searching from '{m_startDirectory}'");
            return null;
        }

        /// <summary>
        /// Parse git config file for the remote Url.
        /// </summary>
        /// <remarks>
        /// Remote named as "upstream" will take the highest precendence.
        /// If there exists no fork in the repo, then the "origin" Url is returned.
        /// If there exists more than one fork and if none of them are named as "upstream", then nothing is returned.
        /// </remarks>
        private string GetGitRemoteUrl(string gitConfigFilePath)
        {
            using StreamReader reader = new StreamReader(gitConfigFilePath);
            var gitRemoteUrl = ParseGitConfigStreamReader(reader);
            if (gitRemoteUrl == null)
            {
                TracingLog.Logger.Log.FailedToCaptureGitRemoteRepoInfo(m_loggingContext, $"Unable to parse the file at path {gitConfigFilePath} and retrieve the remote Url");
            }

            return gitRemoteUrl;
        }

        /// <summary>
        /// Parses the StreamReader to obtain the remote url of the repo.
        /// </summary>
        public static string ParseGitConfigStreamReader(TextReader reader)
        {
            string line = null;
            string currentRemoteName = null;
            string remoteOriginUrl = null;
            int remoteNameCount = 0;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                var match = s_remoteSectionRegex.Match(line);
                // If the pattern matches the remote section then it extracts the remote name and stores it in currentRemoteName
                // Using this we parse the file further until we reach the Url field under that section.
                if (match.Success)
                {
                    currentRemoteName = match.Groups[1].Value;
                    // The counter is incremented to handle the unique scenario where there's an origin and multiple upstreams that haven't been named according to the convention.
                    remoteNameCount++;
                }
                else if (currentRemoteName != null)
                {
                    var matchUrl = s_remoteUrlRegex.Match(line);
                    if (matchUrl.Success)
                    {
                        // Since a remote named as "upstream" is of highest precendece, we can stop parsing the file and return the remote Url of "upstream".
                        if (string.Equals(currentRemoteName, "upstream", StringComparison.Ordinal))
                        {
                            return matchUrl.Groups[1].Value;
                        }

                        if (string.Equals(currentRemoteName, "origin", StringComparison.Ordinal))
                        {
                            remoteOriginUrl = matchUrl.Groups[1].Value;
                        }
                        currentRemoteName = null;
                    }
                }
            }

            // If the repo has only one remote called "origin", we return that origin Url.
            if (remoteNameCount == 1 && remoteOriginUrl != null)
            {
                return remoteOriginUrl;
            }

            return null;
        }
    }
}
