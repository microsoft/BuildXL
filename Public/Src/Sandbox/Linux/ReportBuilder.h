// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BUILDXL_SANDBOX_LINUX_REPORT_BUILDER_H
#define BUILDXL_SANDBOX_LINUX_REPORT_BUILDER_H

#include <limits.h>
#include <string>

#include "FileAccessHelpers.h"
#include "Operations.h"
#include "ReportType.h"
#include "SandboxEvent.h"

namespace buildxl {
namespace linux {

enum class EventReportType {
    kSource = 0,
    kDestination
};

/**
 * Contains a set of static methods that can generate access report strings to be sent back to the managed side.
 */
class ReportBuilder {
private:
    static int SandboxEventToString(
        buildxl::linux::SandboxEvent &event,
        buildxl::linux::AccessReport report,
        char* buffer,
        unsigned int max_length);

public:
    /**
     * Generate a report string for a file operation.
     */
    static bool SandboxEventReportString(
        buildxl::linux::SandboxEvent &event,
        buildxl::linux::AccessReport report,
        char* buffer,
        unsigned int max_length,
        unsigned int &report_length);

    /**
     * Generate a report for a debug message.
     */
    static int DebugReportReportString(
        DebugEventSeverity severity,
        pid_t pid,
        const char* message,
        char* buffer,
        unsigned int max_length);        
};

} // namespace linux
} // namespace buildxl

#endif // BUILDXL_SANDBOX_LINUX_REPORT_BUILDER_H