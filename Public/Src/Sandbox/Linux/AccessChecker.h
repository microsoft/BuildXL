// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef BUILDXL_SANDBOX_LINUX_ACCESS_CHECKER_H
#define BUILDXL_SANDBOX_LINUX_ACCESS_CHECKER_H

#include "FileAccessHelpers.h"
#include "PolicyResult.h"
#include "SandboxEvent.h"
#include "FileAccessManifest.h"
#include "SandboxEvent.h"

namespace buildxl {
namespace linux {

/**
 * Describes the type of access check being performed for a given sandbox event.
*/
enum class CheckerType {
    kExecute,
    kRead,
    kWrite,
    kProbe,
    kUnixAbsentProbe,
    kEnumerateDir,
    kCreateSymlink,
    kCreateDirectory,
    kCreateDirectoryNoEnforcement
};

/**
 * Performs access checks based on a given SandboxEvent type.
 */
class AccessChecker {
public:
    /**
     * Get an access check for the provided SandboxEvent.
     */
    static AccessCheckResult CheckAccessAndGetReport(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
private:
    static AccessCheckResult GetAllowedAccessCheckResult();

    static PolicyResult PolicyForPath(const buildxl::common::FileAccessManifest *fam, const char* absolute_path);
    static PolicySearchCursor FindManifestRecord(const buildxl::common::FileAccessManifest *fam, const char* absolute_path, size_t path_length);

    static std::tuple<AccessCheckResult, PolicyResult> GetResult(const buildxl::common::FileAccessManifest *fam, CheckerType checker, const char* path, bool is_directory);
    static AccessCheckResult GetAccessCheckAndSetProperties(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event, CheckerType checker);

    /** Handler Functions */
    static AccessCheckResult HandleProcessCreate(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleProcessExec(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleProcessExit(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleLookup(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleOpen(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleClose(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleCreate(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleLink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleUnlink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleReadlink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleRename(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleGenericWrite(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleGenericRead(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);
    static AccessCheckResult HandleGenericProbe(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event);

    /** Checker Functions */
    static void PerformAccessCheck(CheckerType type, PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckExecute(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckProbe(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckUnixAbsentProbe(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckRead(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckEnumerateDir(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckWrite(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckCreateSymlink(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckCreateDirectory(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
    static void CheckCreateDirectoryNoEnforcement(PolicyResult policy, bool is_dir, AccessCheckResult *check_result);
};

} // namespace linux
} // namespace buildxl
#endif // BUILDXL_SANDBOX_LINUX_ACCESS_CHECKER_H