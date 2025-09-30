// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "AccessChecker.h"

namespace buildxl {
namespace linux {

/**
 * AccessChecker Handlers
*/
AccessCheckResult AccessChecker::GetAllowedAccessCheckResult() {
    return AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report);
}

PolicyResult AccessChecker::PolicyForPath(const buildxl::common::FileAccessManifest *fam, const char* absolute_path) {
    PolicySearchCursor cursor = FindManifestRecord(fam, absolute_path, /* path_length */ -1);
    if (!cursor.IsValid()) {
        // TODO: Properly log error here
        assert(false && "Invalid policy cursor for path");
    }

    return PolicyResult(fam->GetFlags(), fam->GetExtraFlags(), absolute_path, cursor);
}

PolicySearchCursor AccessChecker::FindManifestRecord(const buildxl::common::FileAccessManifest *fam, const char* absolute_path, size_t path_length) {
    assert(absolute_path[0] == '/');

    const char* path_without_root_sentinel = &absolute_path[1];
    size_t len = path_length == -1 ? strlen(path_without_root_sentinel) : path_length;
    return FindFileAccessPolicyInTreeEx(fam->GetUnixManifestTreeRoot(), path_without_root_sentinel, len);
}

std::tuple<AccessCheckResult, PolicyResult> AccessChecker::GetResult(const buildxl::common::FileAccessManifest *fam, CheckerType checker, const char* path, bool is_directory, bool exists, bool basedOnPolicy) {
    auto policy = PolicyForPath(fam, path);
    AccessCheckResult result = AccessCheckResult::Invalid();

    PerformAccessCheck(checker, policy, is_directory, exists, basedOnPolicy, &result);

    return { result, policy };
}

AccessCheckResult AccessChecker::GetAccessCheckAndSetProperties(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event, CheckerType checker, bool basedOnPolicy) {
    auto [result, policy] = GetResult(fam, checker, event.GetSrcPath().c_str(), event.IsDirectory(), event.PathExists(), basedOnPolicy);
    event.SetSourceAccessCheck(result);
    return result;
}

AccessCheckResult AccessChecker::CheckAccessAndGetReport(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event, bool basedOnPolicy) {
    switch (event.GetEventType())
    {
        case buildxl::linux::EventType::kClone:
            return HandleProcessCreate(fam, event);
        case buildxl::linux::EventType::kExec:
            return HandleProcessExec(fam, event);
        case buildxl::linux::EventType::kExit:
            return HandleProcessExit(fam, event);
        case buildxl::linux::EventType::kOpen:
            return HandleOpen(fam, event);
        case buildxl::linux::EventType::kClose:
            return HandleClose(fam, event);
        case buildxl::linux::EventType::kCreate:
            return HandleCreate(fam, event);
        case buildxl::linux::EventType::kGenericWrite:
            return HandleGenericWrite(fam, event, basedOnPolicy);
        case buildxl::linux::EventType::kGenericRead:
            return HandleGenericRead(fam, event);
        case buildxl::linux::EventType::kGenericProbe:
            return HandleGenericProbe(fam, event);
        case buildxl::linux::EventType::kRename:
            return HandleRename(fam, event);
        case buildxl::linux::EventType::kReadLink:
            return HandleReadlink(fam, event);
        case buildxl::linux::EventType::kLink:
            return HandleLink(fam, event);
        case buildxl::linux::EventType::kUnlink:
            return HandleUnlink(fam, event);
        default: {
            assert(false);
            event.SetSourceAccessCheck(AccessCheckResult::Invalid());
            return AccessCheckResult::Invalid();
        }
    }
}

AccessCheckResult AccessChecker::HandleProcessCreate(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    auto result = GetAllowedAccessCheckResult();
    event.SetSourceAccessCheck(result);
    return result;
}

AccessCheckResult AccessChecker::HandleProcessExec(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    auto result = GetAllowedAccessCheckResult();
    event.SetSourceAccessCheck(result);
    return result;
}

AccessCheckResult AccessChecker::HandleProcessExit(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    auto result = GetAllowedAccessCheckResult();
    event.SetSourceAccessCheck(result);
    return result;
}

AccessCheckResult AccessChecker::HandleOpen(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    CheckerType checker = event.PathExists()
        ? event.IsDirectory() ? CheckerType::kEnumerateDir : CheckerType::kRead
        : CheckerType::kProbe;

    event.SetSourceFileOperation(
        event.PathExists()
        ? event.IsDirectory()
            ? buildxl::linux::FileOperation::kOpenDirectory
            : buildxl::linux::FileOperation::kReadFile
        : buildxl::linux::FileOperation::kProbe
    );

    return GetAccessCheckAndSetProperties(fam, event, checker);
}

AccessCheckResult AccessChecker::HandleClose(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    event.SetSourceFileOperation(buildxl::linux::FileOperation::kClose);
    return GetAccessCheckAndSetProperties(fam, event, CheckerType::kRead);
}

AccessCheckResult AccessChecker::HandleCreate(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    CheckerType checker = CheckerType::kWrite;
    if (event.PathExists())
    {
        mode_t mode = event.GetMode();
        bool enforceDirectoryCreation = CheckDirectoryCreationAccessEnforcement(fam->GetFlags());

        checker =
            S_ISLNK(mode) ? CheckerType::kCreateSymlink
                          : S_ISREG(mode)
                            ? CheckerType::kWrite
                            : enforceDirectoryCreation
                                ? CheckerType::kCreateDirectory
                                : CheckerType::kCreateDirectoryNoEnforcement;
    }

    event.SetSourceFileOperation(event.IsDirectory() ? buildxl::linux::FileOperation::kCreateDirectory : buildxl::linux::FileOperation::kCreateFile);

    return GetAccessCheckAndSetProperties(fam, event, checker);
}

AccessCheckResult AccessChecker::HandleLink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    event.SetSourceFileOperation(buildxl::linux::FileOperation::kCreateHardlinkSource);
    event.SetDestinationFileOperation(buildxl::linux::FileOperation::kCreateHardlinkDest);

    auto source = GetResult(fam, CheckerType::kRead, event.GetSrcPath().c_str(), event.IsDirectory(), event.PathExists());
    auto destination = GetResult(fam, CheckerType::kWrite, event.GetDstPath().c_str(), event.IsDirectory(), event.PathExists());
    auto combined_access_check = AccessCheckResult::Combine(std::get<0>(source), std::get<0>(destination));
    
    event.SetSourceAccessCheck(std::get<0>(source));
    event.SetDestinationAccessCheck(std::get<0>(destination));

    return combined_access_check;
}

AccessCheckResult AccessChecker::HandleUnlink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    event.SetSourceFileOperation(event.IsDirectory() ? buildxl::linux::FileOperation::kRemoveDirectory : buildxl::linux::FileOperation::kDeleteFile);
    event.SetDestinationFileOperation(buildxl::linux::FileOperation::kCreateHardlinkDest);

    return GetAccessCheckAndSetProperties(fam, event, CheckerType::kWrite);
}

AccessCheckResult AccessChecker::HandleReadlink(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    event.SetSourceFileOperation(event.PathExists() ? buildxl::linux::FileOperation::kReadlink : buildxl::linux::FileOperation::kProbe);
    return GetAccessCheckAndSetProperties(fam, event, event.PathExists() ? CheckerType::kRead : CheckerType::kProbe);
}

AccessCheckResult AccessChecker::HandleRename(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    if (event.IsDirectory()) {
        event.SetSourceFileOperation(buildxl::linux::FileOperation::kRemoveDirectory);
        event.SetDestinationFileOperation(buildxl::linux::FileOperation::kCreateDirectory);
    }
    else {
        event.SetSourceFileOperation(buildxl::linux::FileOperation::kDeleteFile);
        event.SetDestinationFileOperation(buildxl::linux::FileOperation::kCreateFile);
    }

    auto source = GetResult(fam, CheckerType::kWrite, event.GetSrcPath().c_str(), event.IsDirectory(), event.PathExists());
    auto destination = GetResult(fam, CheckerType::kWrite, event.GetDstPath().c_str(), event.IsDirectory(), event.PathExists());
    auto combined_access_check = AccessCheckResult::Combine(std::get<0>(source), std::get<0>(destination));

    event.SetSourceAccessCheck(std::get<0>(source));
    event.SetDestinationAccessCheck(std::get<0>(destination));

    return combined_access_check;
}

AccessCheckResult AccessChecker::HandleGenericWrite(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event, bool basedOnPolicy) {
    event.SetSourceFileOperation(buildxl::linux::FileOperation::kWriteFile);
    return GetAccessCheckAndSetProperties(fam, event, CheckerType::kWrite, basedOnPolicy);
}

AccessCheckResult AccessChecker::HandleGenericRead(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    auto operation_type = event.PathExists()
        ? buildxl::linux::FileOperation::kReadFile
        : buildxl::linux::FileOperation::kProbe;

    // Reads on directories are considered enumerations because this operation is used for syscalls like open, and scandir
    // which are either enumerations or a prerequisite for an enumeration that will happen next.
    auto checker = event.PathExists()
        ? event.IsDirectory() ? CheckerType::kEnumerateDir : CheckerType::kRead
        : CheckerType::kProbe;

    event.SetSourceFileOperation(operation_type);
    return GetAccessCheckAndSetProperties(fam, event, checker);
}

AccessCheckResult AccessChecker::HandleGenericProbe(const buildxl::common::FileAccessManifest *fam, buildxl::linux::SandboxEvent &event) {
    event.SetSourceFileOperation(event.PathExists() ? buildxl::linux::FileOperation::kProbe : buildxl::linux::FileOperation::kProbe);
    return GetAccessCheckAndSetProperties(fam, event, event.PathExists() ? CheckerType::kProbe : CheckerType::kProbe);
}

/**
 * Checker Functions
 */
void AccessChecker::PerformAccessCheck(CheckerType type, PolicyResult policy, bool is_dir, bool exists, bool basedOnPolicy, AccessCheckResult *check_result) {
    switch(type) {
        case CheckerType::kExecute:
            CheckExecute(policy, is_dir, check_result);
            break;
        case CheckerType::kRead:
            CheckRead(policy, is_dir, check_result);
            break;
        case CheckerType::kWrite:
            CheckWrite(policy, is_dir, check_result, basedOnPolicy);
            break;
        case CheckerType::kProbe:
            CheckProbe(policy, is_dir, exists, check_result);
            break;
        case CheckerType::kEnumerateDir:
            CheckEnumerateDir(policy, is_dir, check_result);
            break;
        case CheckerType::kCreateSymlink:
            CheckCreateSymlink(policy, is_dir, check_result);
            break;
        case CheckerType::kCreateDirectory:
            CheckCreateDirectory(policy, is_dir, check_result);
            break;
        case CheckerType::kCreateDirectoryNoEnforcement:
            CheckCreateDirectoryNoEnforcement(policy, is_dir, exists, check_result);
            break;
        default:
            assert(false && "Invalid CheckerType");
            break;
    }
}

void AccessChecker::CheckExecute(PolicyResult policy, bool is_dir, AccessCheckResult *check_result) {
    RequestedReadAccess requestedAccess = is_dir
        ? RequestedReadAccess::Probe
        : RequestedReadAccess::Read;
    
    *check_result = policy.CheckReadAccess(requestedAccess, FileReadContext(FileExistence::Existent, is_dir));
}

void AccessChecker::CheckProbe(PolicyResult policy, bool is_dir, bool exists, AccessCheckResult *check_result) {
    *check_result = policy.CheckReadAccess(
        RequestedReadAccess::Probe, 
        exists ? FileReadContext(FileExistence::Existent, is_dir) : FileReadContext(FileExistence::Nonexistent));
}

void AccessChecker::CheckRead(PolicyResult policy, bool is_dir, AccessCheckResult *check_result) {
    *check_result = policy.CheckReadAccess(RequestedReadAccess::Read, FileReadContext(FileExistence::Existent, is_dir));
}

void AccessChecker::CheckEnumerateDir(PolicyResult policy, bool is_dir, AccessCheckResult *check_result) {
    *check_result = AccessCheckResult(
        RequestedAccess::Enumerate,
        ResultAction::Allow,
        policy.ReportDirectoryEnumeration() ? ReportLevel::ReportExplicit : ReportLevel::Ignore);
}

void AccessChecker::CheckWrite(PolicyResult policy, bool is_dir, AccessCheckResult *check_result, bool basedOnPolicy) {
    *check_result = is_dir
        ? policy.CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Existent, is_dir))
        : policy.CheckWriteAccess(basedOnPolicy);
}

void AccessChecker::CheckCreateSymlink(PolicyResult policy, bool is_dir, AccessCheckResult *check_result) {
    *check_result = policy.CheckSymlinkCreationAccess();
}

void AccessChecker::CheckCreateDirectory(PolicyResult policy, bool is_dir, AccessCheckResult *check_result) {
    *check_result = policy.CheckCreateDirectoryAccess();
}

void AccessChecker::CheckCreateDirectoryNoEnforcement(PolicyResult policy, bool is_dir, bool exists, AccessCheckResult *check_result) {
    // CODESYNC: CreateDirectoryW in DetouredFunctions.cpp
    *check_result = policy.CheckCreateDirectoryAccess();
    if (check_result->ShouldDenyAccess()) {
        CheckProbe(policy, is_dir, exists, check_result);
    }
}

} // namespace linux
} // namespace buildxl