// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "Checkers.hpp"

static void checkExecute(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    RequestedReadAccess requestedAccess = isDir
        ? RequestedReadAccess::Probe
        : RequestedReadAccess::Read;
    
    *checkResult = policy.CheckReadAccess(requestedAccess, FileReadContext(FileExistence::Existent, isDir));
}

static void checkProbe(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = policy.CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Existent, isDir));
}

static void checkRead(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    RequestedReadAccess requestedAccess = isDir
        ? RequestedReadAccess::Enumerate
        : RequestedReadAccess::Read;
    
    *checkResult = policy.CheckReadAccess(requestedAccess, FileReadContext(FileExistence::Existent, isDir));
}

static void checkLookup(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = policy.CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Nonexistent));
    checkResult->RequestedAccess = RequestedAccess::Lookup;
}

static void checkEnumerateDir(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = AccessCheckResult(
        RequestedAccess::Enumerate,
        ResultAction::Allow,
        policy.ReportDirectoryEnumeration() ? ReportLevel::ReportExplicit : ReportLevel::Ignore);
}

static void checkWrite(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = isDir
        ? policy.CheckReadAccess(RequestedReadAccess::Probe, FileReadContext(FileExistence::Existent, isDir))
        : policy.CheckWriteAccess();
}

static void checkReadWrite(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    AccessCheckResult readResult = AccessCheckResult::Invalid();
    checkRead(policy, isDir, &readResult);

    AccessCheckResult writeResult = AccessCheckResult::Invalid();
    checkRead(policy, isDir, &writeResult);

    *checkResult = AccessCheckResult::Combine(readResult, writeResult);
}

static void checkCreateSymlink(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = policy.CheckSymlinkCreationAccess();
}

static void checkCreateDirectory(PolicyResult policy, bool isDir, AccessCheckResult *checkResult)
{
    *checkResult = policy.CheckCreateDirectoryAccess();
}

CheckFunc Checkers::CheckRead            = checkRead;
CheckFunc Checkers::CheckLookup          = checkLookup;
CheckFunc Checkers::CheckWrite           = checkWrite;
CheckFunc Checkers::CheckProbe           = checkProbe;
CheckFunc Checkers::CheckExecute         = checkExecute;
CheckFunc Checkers::CheckReadWrite       = checkReadWrite;
CheckFunc Checkers::CheckEnumerateDir    = checkEnumerateDir;
CheckFunc Checkers::CheckCreateSymlink   = checkCreateSymlink;
CheckFunc Checkers::CheckCreateDirectory = checkCreateDirectory;
