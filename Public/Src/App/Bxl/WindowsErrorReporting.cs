// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BuildXL.Utilities;
using Strings = bxl.Strings;

namespace BuildXL
{
    /// <summary>
    /// Helper to submit a custom Windows Error Reporting report.
    /// </summary>
    /// <remarks>
    /// Environment.FailFast() handles much of the functionality here. The native methods are pinvoked in order to allow
    /// greater control over various settings such as attaching additional files.
    /// </remarks>
    public static class WindowsErrorReporting
    {
        // Event name used to look up the bucket hashing algorithm for managed applications.
        private const string EventType = "CLR20r3";

        /// <summary>
        /// Maximum size of files to be uploaded. 20 MB
        /// </summary>
        private const int MaxFileSize = 20 * 1024 * 1024;

        /// <summary>
        /// Annotates a dump for <c>WER</c>.
        /// </summary>
        public static void CreateDump(Exception ex, BuildInfo buildInfo, IEnumerable<string> filesToAttach, string sessionId)
        {
            try
            {
                // Set up parameters to mirror those generally set by the CLR when a managed application crashes.
                List<KeyValuePair<string, string>> parameters = ComputeParameters(ex, buildInfo, sessionId);

                // Create a report
                IntPtr report = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(NativeMethods.WerReportCreate(EventType, NativeMethods.WER_REPORT_TYPE.WerReportApplicationCrash, IntPtr.Zero, ref report));
                if (report == IntPtr.Zero)
                {
                    // WerReportCreate() should have returned a non-zero HResult causing an exception to be thrown above, so this most likely won't get hit
                    throw new BuildXLException("Failed to create a Windows Error Reporting report");
                }

                // Set the parameters
                int index = 0;
                foreach (var kvp in parameters)
                {
                    if (kvp.Key == null || kvp.Value == null)
                    {
                        continue;
                    }

                    // WER has a max lengh of 255 for each parameter value
                    string value = kvp.Value.Length > 255 ? kvp.Value.Substring(0, 255) : kvp.Value;
                    Marshal.ThrowExceptionForHR(NativeMethods.WerReportSetParameter(report, index, kvp.Key, value));
                    index++;
                }

                // Add the dump
                Marshal.ThrowExceptionForHR(NativeMethods.WerReportAddDump(
                    report,
#if FEATURE_SAFE_PROCESS_HANDLE
                    Process.GetCurrentProcess().SafeHandle,
#else
                    Process.GetCurrentProcess().Handle,
#endif
                    IntPtr.Zero,
                    NativeMethods.WER_DUMP_TYPE.WerDumpTypeHeapDump,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0));

                AddDebuggingFile(report, ex);

                // Attach any extra files
                if (filesToAttach != null)
                {
                    foreach (string file in filesToAttach)
                    {
                        WerReportAddFileWithMaxLength(report, file);
                    }
                }

                // Submit the report and close the handle
                IntPtr sendResult = IntPtr.Zero;
                Marshal.ThrowExceptionForHR(NativeMethods.WerReportSubmit(report, NativeMethods.WER_CONSENT.WerConsentNotAsked, 0, ref sendResult));
                Marshal.ThrowExceptionForHR(NativeMethods.WerReportCloseHandle(report));
            }
            catch (Exception werException)
            {
                // Fall back on the standard FailFast in case there was an error in the custom WER reporting
                Environment.FailFast(Strings.App_AppDomain_UnhandledException, new AggregateException("Failed to create custom Windows Error Reporting report", werException, ex));
            }
        }

        /// <summary>
        /// Adds a file to a WER report with a maximum file length
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "StreamReader/StreamWriter takes ownership for disposal.")]
        private static void WerReportAddFileWithMaxLength(IntPtr report, string filePath, int maxLength = MaxFileSize)
        {
            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                FileInfo info = new FileInfo(filePath);

                // If the file is larger than the maximum allowed, copy the tail to a temp file
                if (info.Length > maxLength)
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), Path.GetFileName(filePath));
                    using (FileStream sourceStream = File.OpenRead(filePath))
                    {
                        sourceStream.Position = sourceStream.Length - maxLength;
                        using (StreamWriter writer = new StreamWriter(File.Open(tempFile, FileMode.Create, FileAccess.Write)))
                        {
                            sourceStream.CopyTo(writer.BaseStream);
                        }
                    }

                    // Instruct WER to delete the temp file when its done
                    Marshal.ThrowExceptionForHR(NativeMethods.WerReportAddFile(
                        report,
                        tempFile,
                        NativeMethods.WER_FILE_TYPE.WerFileTypeOther,
                        NativeMethods.WER_FILE_FLAGS.WER_FILE_DELETE_WHEN_DONE));
                }
                else
                {
                    Marshal.ThrowExceptionForHR(NativeMethods.WerReportAddFile(
                        report,
                        filePath,
                        NativeMethods.WER_FILE_TYPE.WerFileTypeOther,
                        NativeMethods.WER_FILE_FLAGS.None));
                }
            }
        }

        /// <summary>
        /// Creates a temporary file with any additional debugging info for the crash and adds it to the WER report
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "StreamReader/StreamWriter takes ownership for disposal.")]
        private static void AddDebuggingFile(IntPtr report, Exception ex)
        {
            string file = Path.Combine(Path.GetTempPath(), "BuildXLDebug.txt");

            using (StreamWriter writer = new StreamWriter(File.Open(file, FileMode.Create, FileAccess.Write)))
            {
                writer.WriteLine(CurrentProcess.GetCommandLine());
                writer.WriteLine(ex.ToStringDemystified());
            }

            Marshal.ThrowExceptionForHR(NativeMethods.WerReportAddFile(
                report,
                file,
                NativeMethods.WER_FILE_TYPE.WerFileTypeOther,
                NativeMethods.WER_FILE_FLAGS.WER_FILE_DELETE_WHEN_DONE));
        }

        /// <summary>
        /// Computes a list of parameters to mostly mirror those set by the CLR when a managed application crashes.
        /// </summary>
        /// <remarks>
        /// These parameters are set:
        /// P0:AppName=bxl.exe
        /// P1:AppVer=FileVersion of bxl.exe
        /// P2:AppStamp=CLR sets this to IMAGE_NT_HEADERS.FileHeader.TimeDateStamp. Set to git commit id retrieved from bxl.exe file metadata
        /// P3:AsmAndModName=Name of assembly throwing the exception
        /// P4:AsmVer=FileVersion of assembly throwing exception
        /// P5:ModStamp=CLR sets this to IMAGE_NT_HEADERS.FileHeader.TimeDateStamp. Set to git commit id retrieved from bxl.exe file metadata
        /// P6:MethodDef=Name of method throwing the exception
        /// P7:Offset=The IL offset to what throws the exception
        /// P8:ExceptionType=Name of the exception type
        /// P9:SessionId
        ///
        /// These parameters can be set to any value but the intent is to mirror what the CLR would set in Environment.FailFast().
        /// This will make the error reports created by this custom code fall into the same searchable fault buckets as
        /// the ones created by the CLR.
        ///
        /// P9 isn't used by the CLR crash type. So we use that to stuff in the sessionId to be able to cross reference
        /// telemetry crashes with watson
        ///
        /// Note that depending on what you're using to see watson results, these may start at P0 or P1. So if you're
        /// after "SessonId", you should look at both P9 and P10
        /// </remarks>
        private static List<KeyValuePair<string, string>> ComputeParameters(Exception ex, BuildInfo buildInfo, string sessionId)
        {
            // Compute module and exception information based on the exception
            string asmAndModName = string.Empty;
            string asmVer = string.Empty;
            string methodDef = string.Empty;
            string offset = "0";
            string exceptionType = string.Empty;
            if (ex != null)
            {
                StackTrace st = new StackTrace(ex, false);
                StackFrame frame = st.GetFrames()[0];
                if (frame != null)
                {
                    offset = frame.GetILOffset().ToString(CultureInfo.InvariantCulture);
                }

                var targetSite = ex.TargetSite;
                if (targetSite != null)
                {
                    if (targetSite.Name != null)
                    {
                        methodDef = targetSite.Name;
                    }

                    if (targetSite.Module != null && targetSite.Module.Assembly != null)
                    {
                        AssemblyName name = targetSite.Module.Assembly.GetName();
                        if (name != null)
                        {
                            asmAndModName = name.Name;
                            asmVer = name.Version.ToString();
                        }
                    }
                }

                if (ex.GetType() != null)
                {
                    exceptionType = ex.GetType().Name;
                }
            }

            return new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("AppName", Branding.ProductExecutableName),
                new KeyValuePair<string, string>("AppVer", buildInfo.FileVersionAccountingForDevBuilds),
                new KeyValuePair<string, string>("AppStamp", buildInfo.CommitId),
                new KeyValuePair<string, string>("AsmAndModName", asmAndModName),
                new KeyValuePair<string, string>("AsmVer", asmVer),

                // Use the same stamp for the module as the AppStamp
                new KeyValuePair<string, string>("ModStamp", buildInfo.CommitId),
                new KeyValuePair<string, string>("MethodDef", methodDef),
                new KeyValuePair<string, string>("Offset", offset),
                new KeyValuePair<string, string>("ExceptionType", exceptionType),
                new KeyValuePair<string, string>("SessionId", sessionId),
            };
        }

        /// <summary>
        /// See http://msdn.microsoft.com/en-us/library/bb513635(v=vs.85).aspx
        /// for documentation of WER methods
        /// </summary>
        private static class NativeMethods
        {
            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int WerReportAddDump(
                IntPtr hReportHandle,
#if FEATURE_SAFE_PROCESS_HANDLE
                SafeHandle hProcess,
#else
                IntPtr hProcess,
#endif
                IntPtr hThread,
                WER_DUMP_TYPE dumpType,
                IntPtr pExceptionParam,
                IntPtr pDumpCustomOptions,
                int dwFlags);

            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int WerReportCreate(
                string pwzEventType,
                WER_REPORT_TYPE repType,
                IntPtr pReportInformation,
                ref IntPtr phReportHandle);

            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int WerReportSetParameter(
                IntPtr hReportHandle,
                int dwparamID,
                string pwzName,
                string pwzValue);

            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int WerReportSubmit(
                IntPtr hReportHandle,
                WER_CONSENT consent,
                int dwFlags,
                ref IntPtr pSubmitResult);

            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            internal static extern int WerReportAddFile(
                IntPtr hReportHandle,
                string pwzPath,
                WER_FILE_TYPE repFileType,
                NativeMethods.WER_FILE_FLAGS dwFlags);

            [DllImport("wer.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern int WerReportCloseHandle(IntPtr reportHandle);

            internal enum WER_CONSENT
            {
                WerConsentAlwaysPrompt = 4,
                WerConsentApproved = 2,
                WerConsentDenied = 3,
                WerConsentMax = 5,
                WerConsentNotAsked = 1,
            }

            internal enum WER_DUMP_TYPE
            {
                WerDumpTypeHeapDump = 3,
                WerDumpTypeMax = 4,
                WerDumpTypeMicroDump = 1,
                WerDumpTypeMiniDump = 2,
            }

            internal enum WER_REPORT_TYPE
            {
                WerReportNonCritical = 0,
                WerReportCritical = 1,
                WerReportApplicationCrash = 2,
                WerReportApplicationHang = 3,
                WerReportKernel = 4,
                WerReportInvalid = 5,
            }

            internal enum WER_FILE_TYPE
            {
                WerFileTypeMicrodump = 1,
                WerFileTypeMinidump = 2,
                WerFileTypeHeapdump = 3,
                WerFileTypeUserDocument = 4,
                WerFileTypeOther = 5,
            }

            [Flags]
            public enum WER_FILE_FLAGS
            {
                None = 0,
                WER_FILE_DELETE_WHEN_DONE = 1,
                WER_FILE_ANONYMOUS_DATA = 2,
            }
        }
    }
}
