// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using BuildXL.Native.IO;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities.Instrumentation.Common;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Helper class for writing tests that deal with Access Control Lists and File Ownership
    /// </summary>
    public static class ACLHelpers
    {
        /// <summary>
        /// Revokes access to a file or directory
        /// </summary>
        /// <param name="testFilePath"></param>
        public static void RevokeAccess(string testFilePath)
        {
            string icaclsResult;
            if (RunIcacls($"{testFilePath} /setowner SYSTEM", out icaclsResult) != 0)
            {
                throw new BuildXLTestException($"Failed to reset file owner: {Environment.NewLine}{icaclsResult}");
            }

            // Deny access to this account
            if (RunIcacls($"{testFilePath} /deny {Environment.UserDomainName}\\{Environment.UserName}:(GA) /inheritance:r", out icaclsResult) != 0)
            {
                throw new BuildXLTestException($"Failed to reset filesystem ACLs: {Environment.NewLine}{icaclsResult}");
            }
        }

        /// <summary>
        /// Revokes access using the SetPrivilege native methods instead of calling icacls
        /// </summary>
        public static void RevokeAccessNative(string testFilePath, LoggingContext loggingContext)
        {
            if (testFilePath.Length > NativeIOConstants.MaxPath)
            {
                testFilePath = FileSystemWin.LongPathPrefix + testFilePath;
            }

            // Restore name privilege is required to change the owner of the file
            FileUtilitiesWin.NativeMethods.SetPrivilege(FileUtilitiesWin.NativeMethods.SE_RESTORE_NAME, enablePrivilege: true, testFilePath, loggingContext);
            
            var fileInfo = new FileInfo(testFilePath);
            var fileSecurity = fileInfo.GetAccessControl();

            // Remove any existing rules for the current user
            fileSecurity.RemoveAccessRuleAll(
                    new FileSystemAccessRule(
                        $"{Environment.UserDomainName}\\{Environment.UserName}",
                        FileSystemRights.FullControl,
                        AccessControlType.Allow));

            // Add a new deny rule
            fileSecurity.AddAccessRule(
                    new FileSystemAccessRule(
                        $"{Environment.UserDomainName}\\{Environment.UserName}",
                        FileSystemRights.FullControl,
                        AccessControlType.Deny));

            // Update the owner to SYSTEM
            fileSecurity.SetOwner(new NTAccount(@"NT AUTHORITY\SYSTEM"));
            fileInfo.SetAccessControl(fileSecurity);
            
            // Remove restore name privilege once complete
            FileUtilitiesWin.NativeMethods.SetPrivilege(FileUtilitiesWin.NativeMethods.SE_RESTORE_NAME, enablePrivilege: false, testFilePath, loggingContext);
        }

        private static int RunIcacls(string arguments, out string result)
        {
            var psi = new ProcessStartInfo();
            psi.FileName = "icacls";
            psi.Arguments = arguments;
            psi.CreateNoWindow = true;
            psi.UseShellExecute = false;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;

            using (Process proc = new Process())
            {
                StringBuilder outputStream = new StringBuilder();
                proc.OutputDataReceived += proc_OutputDataReceived;
                proc.ErrorDataReceived += proc_OutputDataReceived;

                proc.StartInfo = psi;
                proc.Start();
                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();

                if (!proc.WaitForExit(10 * 1000))
                {
                    proc.Kill();
                }

                proc.WaitForExit();

                lock (outputStream)
                {
                    result = outputStream.ToString();
                }
                return proc.ExitCode;

                void proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
                {
                    lock (outputStream)
                    {
                        outputStream.AppendLine(e.Data);
                    }
                }
            }
        }
    }
}
