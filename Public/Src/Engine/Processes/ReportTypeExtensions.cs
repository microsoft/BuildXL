// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Processes
{
    /// <summary>
    /// Extensions for <see cref="ReportType"/>.
    /// </summary>
    internal static class ReportTypeExtensions
    {
        /// <summary>
        /// Returns true if the report type should be counted for Detours message validation.
        /// </summary>
        /// <remarks>
        /// Currently on Detours side, the semaphore is only release for <see cref="ReportType.FileAccess"/>, <see cref="ReportType.ProcessData"/>, and
        /// <see cref="ReportType.ProcessDetouringStatus"/> (see all uses of `SendReportString` in \Public\Src\Sandbox\Windows\DetoursServices\SendReport.cpp).
        /// So, only those three <see cref="ReportType"/>s are included currently.
        /// 
        /// <see cref="ReportType.WindowsCall"/> is not included because the report type is currently not supported (see <seealso cref="SandboxedProcessReports"/>).
        /// 
        /// <see cref="ReportType.DebugMessage"/> is not included because the report type does not affect the build correctness, i.e., missing some debug
        /// messages will not make the build incorrect. Moreover, the report type itself can be used (in the future) to debug when there is a missing message of
        /// another type. So, we don't want the missing of <see cref="ReportType.DebugMessage"/> message to interfere with the debugging process.
        /// 
        /// TODO: BUG1903935
        ///       <see cref="ReportType.AugmentedFileAccess"/> is currently not included because such messages do not release the semaphore.
        ///       <see cref="ReportType.AugmentedFileAccess"/> messages need to be counted for validation because missing such messages can result in incorrect
        ///       observations, which subsequently results in incorrect builds.
        ///
        ///       If we try to include it here, but the <see cref="AugmentedManifestReporter"/> does not release the semaphore, then such missing messages can go undetected.
        ///       Suppose that you have the following sequence of reports:
        ///
        ///           FileAccess(f), FileAccess(g), AugmentedFileAccess(a), FileAccess(h).
        ///
        ///       Suppose further that, due to broken pipe, FileAccess(h) sent by Detours never arrives here. The semaphore has been released 3 times.
        ///       However, because there's AugmentedFileAccess(a), the semaphore's counts gets 0 and satisfies the Detours validation.
        ///       To correct this issue, <see cref="AugmentedManifestReporter"/> must release the semaphore when reporting a file access.
        /// </remarks>
        public static bool ShouldCountReportType(this ReportType reportType) =>
            reportType == ReportType.FileAccess
            || reportType == ReportType.ProcessData
            || reportType == ReportType.ProcessDetouringStatus;
            // TODO: || reportType == ReportType.AugmentedFileAccess;
    }
}
