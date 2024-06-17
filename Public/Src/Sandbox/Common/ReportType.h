// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BUILDXL_SANDBOX_LINUX_REPORT_TYPE_H
#define BUILDXL_SANDBOX_LINUX_REPORT_TYPE_H

namespace buildxl {
namespace common {

// CODESYNC: Public/Src/Engine/Processes/ReportType.cs
enum class ReportType
{
    kNone = 0,
    kFileAccess = 1,
    kWindowsCall = 2,
    kDebugMessage = 3,
    kProcessData = 4,
    kProcessDetouringStatus = 5,
    kAugmentedFileAccess = 6,
    kMax = 7,
};

} // namespace common
} // namespace buildxl

#endif // BUILDXL_SANDBOX_LINUX_REPORT_TYPE_H