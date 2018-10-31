// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Text;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Helper class to compare file contents
    /// </summary>
    public static class FileComparison
    {
        /// <summary>
        /// Helper to compare large strings and provide a meaningful error on diffs.
        /// </summary>
        public static bool ValidateContentsAreEqual(string expected, string actual, string expectedFilePath, out string message)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                string[] expectedSplit = expected.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
                string[] actualSplit = actual.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

                int diffLine = 0;
                for (int i = 0; i < Math.Max(expectedSplit.Length, actualSplit.Length); i++)
                {
                    if (i >= expectedSplit.Length || i >= actualSplit.Length || expectedSplit[i] != actualSplit[i])
                    {
                        diffLine = i;
                        break;
                    }
                }

                string expectedFileForDiffing = expectedFilePath;
                if (string.IsNullOrEmpty(expectedFileForDiffing))
                {
                    // if the expected file is not given, write it to a temp file.
                    expectedFileForDiffing = Path.GetTempFileName();
                    File.WriteAllText(expectedFileForDiffing, expected);
                }

                var actualFileForDiffing = Path.GetTempFileName();
                File.WriteAllText(actualFileForDiffing, actual);

                var messageBuilder = new StringBuilder();
                messageBuilder.AppendLine();
                messageBuilder.AppendFormat(
                    "============ Diff Report for: {0} ============",
                    expectedFilePath == null ? string.Empty : Path.GetFileName(expectedFilePath));
                messageBuilder.AppendLine();

                messageBuilder.AppendFormat("Strings first differ at line:[{0}]. ", diffLine + 1);
                messageBuilder.AppendLine();

                if (diffLine < Math.Min(expectedSplit.Length, actualSplit.Length))
                {
                    messageBuilder.AppendLine("Expected Line:<");
                    messageBuilder.AppendLine(expectedSplit[diffLine]);
                    messageBuilder.AppendLine("> Actual Line:<");
                    messageBuilder.AppendLine(actualSplit[diffLine]);
                    messageBuilder.AppendLine(">");
                }

                messageBuilder.AppendLine("Compare the files via (must build with /cleanTempDirectories- to ensure files are not cleaned):");
                messageBuilder.AppendFormat("odd \"{0}\" \"{1}\"", expectedFileForDiffing, actualFileForDiffing);
                messageBuilder.AppendLine();

                messageBuilder.AppendFormat("Expected:<{0}>. Actual:<{1}>.", expected, actual);

                messageBuilder.AppendLine("============ End of Diff Report ============");

                message = messageBuilder.ToString();
                return false;
            }

            message = null;
            return true;
        }
    }
}
