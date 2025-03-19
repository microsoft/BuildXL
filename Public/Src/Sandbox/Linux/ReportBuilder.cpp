// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <algorithm>
#include <cstring>

#include "ReportBuilder.h"
#include "ReportType.h"

namespace buildxl {
namespace linux {

/**
 * 1. Report Type
 * 2. System call name
 * 3. File Operation
 * 4. Process ID
 * 5. Parent Process ID
 * 6. Error
 * 7. Requested Access
 * 8. File Access Status
 * 9. Report Explicitly
 * 10. Is Directory
 * 11. Is path truncated
 * 12. Path
 */
const char *kFileAccessReportFormat = "%d|%s|%d|%d|%d|%d|%d|%d|%d|%d|%d|%s\n";

/**
 * 1. Report Type
 * 2. System call name
 * 3. File Operation
 * 4. Process ID
 * 5. Parent Process ID
 * 6. Error
 * 7. Requested Access
 * 8. File Access Status
 * 9. Report Explicitly
 * 10. Is Directory
 * 11. Is path truncated
 * 12. Path
 * 13. CommandLineArguments
 */
const char *kProcessExecReportFormat = "%d|%s|%d|%d|%d|%d|%d|%d|%d|%d|%d|%s|%s\n";

/**
 * 1. Report Type
 * 2. Process ID
 * 3. Severity
 * 4. Message
 */
const char *kDebugMessageReportFormat = "%d|%d|%d|%s\n";

int ReportBuilder::SandboxEventToString(
    buildxl::linux::SandboxEvent &event, 
    buildxl::linux::AccessReport report, 
    char* buffer,
    unsigned int max_length)
{
    return SandboxEventToStringInternal(event, report, buffer, max_length, report.path, /* isPathTruncated*/ false);
}

int ReportBuilder::SandboxEventWithTruncatedPathToString(
    buildxl::linux::SandboxEvent &event, 
    buildxl::linux::AccessReport report, 
    char* buffer,
    unsigned int max_length,
    std::string& truncatedPath)
{
    return SandboxEventToStringInternal(event, report, buffer, max_length, truncatedPath, /* isPathTruncated*/ true);
}

int ReportBuilder::SandboxEventToStringInternal(
    buildxl::linux::SandboxEvent &event, 
    buildxl::linux::AccessReport report, 
    char* buffer,
    unsigned int max_length,
    std::string& path,
    bool isPathTruncated) 
{
    // TODO [pgunasekara]: use std::format when it's available with gcc 13+
    switch (event.GetEventType()) {
        case buildxl::linux::EventType::kExec: {
            return snprintf(buffer, max_length, kProcessExecReportFormat,
                buildxl::common::ReportType::kFileAccess,
                event.GetSystemCall(),
                report.file_operation,
                event.GetPid(),
                event.GetParentPid(),
                event.GetError(),
                (unsigned int)report.access_check_result.Access,
                report.access_check_result.GetFileAccessStatus(),
                report.access_check_result.Level == ReportLevel::ReportExplicit,
                event.IsDirectory(),
                isPathTruncated,
                path.c_str(),
                event.GetCommandLine().c_str()
            );
        }
        default: {
            return snprintf(buffer, max_length, kFileAccessReportFormat,
                buildxl::common::ReportType::kFileAccess,
                event.GetSystemCall(),
                report.file_operation,
                event.GetPid(),
                event.GetParentPid(),
                event.GetError(),
                (unsigned int)report.access_check_result.Access,
                report.access_check_result.GetFileAccessStatus(),
                report.access_check_result.Level == ReportLevel::ReportExplicit,
                event.IsDirectory(),
                isPathTruncated,
                path.c_str()
            );
        }
    }
}

bool ReportBuilder::SandboxEventReportString(buildxl::linux::SandboxEvent &event, buildxl::linux::AccessReport report, char* buffer, unsigned int max_length, unsigned int &report_length) {
    int prefix_len = sizeof(unsigned int);
    int max_report_len = max_length - prefix_len;
    int report_string_len = SandboxEventToString(event, report, &buffer[prefix_len], max_report_len);

    // File access reports cannot exceed the max length for a string that fits into a pipe buffer
    if (report_string_len >= max_report_len) {
        // This is very likely caused by a path that is too big. Today we are limiting a message by PATH_MAX. This is a problem when tools try to use paths bigger than that.
        // One solution is to allow splitting the report into multiple events and putting those together on managed side. Today we don't support that functionality.
        // Send the path truncated but indicate that truncation happened so managed side can make a decision from it.
        if (report_string_len - report.path.length() < max_report_len)
        {
            int truncated_size = report.path.length() - (report_string_len - max_report_len) - 1;
            
            // Truncate the path and build a new report now indicating that the path has been truncated.
            auto truncatedPath = report.path.substr(0, truncated_size);
            report_string_len = SandboxEventWithTruncatedPathToString(event, report, &buffer[prefix_len], max_report_len, truncatedPath);
            if (report_string_len >= max_report_len)
            {
                // This should never happen. The error will be caught on the caller anyway.
                return false;
            }
        }
        else 
        {
            return false;
        }
    }

    // Set the prefix with the report length
    *(unsigned int*)buffer = report_string_len;
    report_length = report_string_len + prefix_len;
    return true;
}

int ReportBuilder::DebugReportReportString(DebugEventSeverity severity, pid_t pid, const char* message, char* buffer, unsigned int max_length) {
    int prefix_len = sizeof(unsigned int);
    int max_report_len = max_length - prefix_len;

    int report_string_len = snprintf(
        &buffer[prefix_len],
        max_report_len,
        kDebugMessageReportFormat,
        buildxl::common::ReportType::kDebugMessage,
        pid,
        (int)severity,
        message);

    if (report_string_len >= max_length) {
        // For debug messages it's acceptable to truncate the message
        // We calculate the maximum size allowed, considering that 'message' is the last component of the
        // message (plus the \n that ends any report, hence the -1), so it's the last thing 
        // we tried to write when hitting the size limit.
        int truncated_size = strlen(message) - (report_string_len - max_report_len) - 1;
        char truncated_message[truncated_size] = { 0 };

        // Let's leave an ending \0
        strncpy(truncated_message, message, truncated_size - 1);

        report_string_len = snprintf(
            &buffer[prefix_len],
            max_report_len,
            kDebugMessageReportFormat,
            buildxl::common::ReportType::kDebugMessage,
            pid,
            (int)severity,
            truncated_message);
    }

    // Set the prefix with the report length
    *(unsigned int*)buffer = report_string_len;
    return report_string_len + prefix_len;
}

} // namespace linux
} // namespace buildxl