// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef IOHandler_hpp
#define IOHandler_hpp

#include "AccessHandler.hpp"
#include "IOEvent.hpp"

struct IOHandler final : public AccessHandler
{
public:

    IOHandler(Sandbox *sandbox) : AccessHandler(sandbox) { }

    // The event is processed so a check access result is performed and the access report is returned
    // as an out param. The report is not sent.
    // Some events may result in a pair of reports (the typical case is an operation that involves a source and a 
    // destination). To avoid allocations related to arrays/vectors, an AccessReportGroup is returned, representing
    // one or two access reports. The first access in the group is always valid. The second access may not be, and in
    // that case `shouldReport` member in the second report will be false
    AccessCheckResult CheckAccessAndBuildReport(const IOEvent &event, AccessReportGroup &accessToReportGroup);

    // The provided event is reported via the sandbox send report callback
    AccessCheckResult HandleEvent(const IOEvent &event);

    // This is an overload of HandleProcessUntracked(const pid_t pid, AccessReport &accessToReport) that
    // sends out the report via the sandbox send report callback. This method is used by the Mac machinery.
    AccessCheckResult HandleProcessUntracked(const pid_t pid);

protected:
#pragma mark Process life cycle

    AccessCheckResult HandleProcessFork(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleProcessExec(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleProcessExit(const IOEvent &event, AccessReport &processExitReport, AccessReport &processTreeCompletedReport);

    AccessCheckResult HandleProcessUntracked(const pid_t pid, AccessReport &accessToReport);

#pragma mark Process I/O observation

    AccessCheckResult HandleLookup(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleOpen(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleClose(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleCreate(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleLink(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport);

    AccessCheckResult HandleUnlink(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleReadlink(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleRename(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport);

    AccessCheckResult HandleClone(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport);

    AccessCheckResult HandleExchange(const IOEvent &event, AccessReport &sourceAccessToReport, AccessReport &destinationAccessToReport);

    AccessCheckResult HandleGenericWrite(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleGenericRead(const IOEvent &event, AccessReport &accessToReport);

    AccessCheckResult HandleGenericProbe(const IOEvent &event, AccessReport &accessToReport);
};

#endif /* IOHandler_hpp */
