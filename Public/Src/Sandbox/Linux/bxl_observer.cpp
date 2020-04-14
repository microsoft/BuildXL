// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "dirent.h"
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <dlfcn.h>
#include <unistd.h>
#include <sys/types.h>

#include "bxl_observer.hpp"
#include "IOHandler.hpp"

static std::string empty_str("");

static void HandleAccessReport(AccessReport report, int _)
{
    BxlObserver::GetInstance()->SendReport(report);
}

AccessCheckResult BxlObserver::sNotChecked = AccessCheckResult::Invalid();

BxlObserver* BxlObserver::GetInstance()
{
    static BxlObserver s_singleton;
    return &s_singleton;
}

BxlObserver::BxlObserver()
{
    real_readlink("/proc/self/exe", progFullPath_, PATH_MAX);
    InitFam();
    InitLogFile();
}

void BxlObserver::InitFam()
{
    // read FAM env var
    const char *famPath = getenv(BxlEnvFamPath);
    if (!(famPath && *famPath))
    {
        real_fprintf(stderr, "[%s] ERROR: Env var '%s' not set\n", __func__, BxlEnvFamPath);
        return;
    }

    // read FAM 
    FILE *famFile = real_fopen(famPath, "rb");
    if (!famFile)
    {
        _fatal("Could not open file '%s'; errno: %d", famPath, errno);
    }

    fseek(famFile, 0, SEEK_END);
    long famLength = ftell(famFile);
    rewind(famFile);

    char *famPayload = (char *)malloc(famLength);
    real_fread(famPayload, famLength, 1, famFile);
    real_fclose(famFile);

    // create SandboxedPip (which parses FAM and throws on error)
    pip_ = std::shared_ptr<SandboxedPip>(new SandboxedPip(getpid(), famPayload, famLength));

    // create sandbox
    sandbox_ = new Sandbox(0, Configuration::DetoursLinuxSandboxType);

    // initialize sandbox
    if (!sandbox_->TrackRootProcess(pip_))
    {
        _fatal("Could not track root process %s:%d", __progname, getpid());
    }

    process_ = sandbox_->FindTrackedProcess(getpid());
    process_->SetPath(progFullPath_);
    sandbox_->SetAccessReportCallback(HandleAccessReport);
}

void BxlObserver::InitLogFile()
{
    const char *logPath = getenv(BxlEnvLogPath);
    if (logPath && *logPath)
    {
        strlcpy(logFile_, logPath, PATH_MAX);
        logFile_[PATH_MAX-1] = '\0';
    }
    else
    {
        logFile_[0] = '\0';
    }
}

bool BxlObserver::Send(const char *buf, size_t bufsiz)
{
    if (!real_open)
    {
        _fatal("syscall 'open' not found; errno: %d", errno);
    }

    // TODO: instead of failing, implement a critical section
    if (bufsiz > PIPE_BUF)
    {
        _fatal("Cannot atomically send a buffer whose size (%ld) is greater than PIPE_BUF (%d)", bufsiz, PIPE_BUF);
    }

    const char *reportsPath = GetReportsPath();
    int logFd = real_open(reportsPath, O_WRONLY | O_APPEND, 0);
    if (logFd == -1)
    {
        _fatal("Could not open file '%s'; errno: %d", reportsPath, errno);
    }

    ssize_t numWritten = real_write(logFd, buf, bufsiz);
    if (numWritten < bufsiz)
    {
        _fatal("Wrote only %ld bytes out of %ld", numWritten, bufsiz);
    }

    real_close(logFd);
    return true;
}

bool BxlObserver::SendReport(AccessReport &report)
{
    // there is no central sendbox process here (i.e., there is an instance of this 
    // guy in every child process), so counting process tree size is not feasible
    if (report.operation == FileOperation::kOpProcessTreeCompleted)
    {
        return true;
    }

    const int PrefixLength = sizeof(uint);
    char buffer[PIPE_BUF] = {0};
    int maxMessageLength = PIPE_BUF - PrefixLength;
    int numWritten = snprintf(
        &buffer[PrefixLength], maxMessageLength, "%s|%d|%d|%d|%d|%d|%d|%s\n", 
        __progname, getpid(), report.requestedAccess, report.status, report.reportExplicitly, report.error, report.operation, report.path);
    if (numWritten == maxMessageLength)
    {
        // TODO: once 'send' is capable of sending more than PIPE_BUF at once, allocate a bigger buffer and send that
        _fatal("Message truncated to fit PIPE_BUF (%d): %s", PIPE_BUF, buffer);
    }

    LOG_DEBUG("Sending report: %s", &buffer[PrefixLength]);
    *(uint*)(buffer) = numWritten;
    return Send(buffer, numWritten + PrefixLength);
}

AccessCheckResult BxlObserver::report_access(const char *syscallName, es_event_type_t eventType, std::string reportPath, std::string secondPath)
{
    // TODO: don't stat all the time
    struct stat s;
    mode_t mode = real___lxstat(1, reportPath.c_str(), &s) == 0
        ? s.st_mode
        : 0;

    std::string execPath = eventType == ES_EVENT_TYPE_NOTIFY_EXEC
        ? reportPath
        : std::string(progFullPath_);

    IOEvent event(getpid(), 0, getppid(), eventType, reportPath, secondPath, execPath, mode, false);
    return report_access(syscallName, event);
}

AccessCheckResult BxlObserver::report_access(const char *syscallName, IOEvent &event)
{
    es_event_type_t eventType = event.GetEventType();

    AccessCheckResult result = sNotChecked;

    if (IsValid())
    {
        IOHandler handler(sandbox_);
        handler.SetProcess(process_);
        result = handler.HandleEvent(event);
    }

    LOG_DEBUG("(( %10s:%2d )) %s %s%s", syscallName, event.GetEventType(), event.GetEventPath(), 
        result.ShouldDenyAccess() ? "[Denied]" : "",
        result.ShouldDenyAccess() && IsFailingUnexpectedAccesses() ? "[Blocked]" : "");

    return result;
}

AccessCheckResult BxlObserver::report_access(const char *syscallName, es_event_type_t eventType, const char *pathname)
{
    return report_access(syscallName, eventType, normalize_path(pathname), "");
}

AccessCheckResult BxlObserver::report_access_fd(const char *syscallName, es_event_type_t eventType, int fd)
{
    char fullpath[PATH_MAX] = {0};
    fd_to_path(fd, fullpath, PATH_MAX);

    return fullpath[0] == '/'
        ? report_access(syscallName, eventType, std::string(fullpath), empty_str)
        : sNotChecked; // this file descriptor is not a non-file (e.g., a pipe, or socket, etc.) so we don't care about it
}

AccessCheckResult BxlObserver::report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname)
{
    char fullpath[PATH_MAX] = {0};
    ssize_t len = 0;

    if (dirfd == AT_FDCWD)
    {
        getcwd(fullpath, PATH_MAX);
        len = strlen(fullpath);
    }
    else
    {
        len = fd_to_path(dirfd, fullpath, PATH_MAX);
    }

    if (len <= 0)
    {
        _fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
    }

    snprintf(&fullpath[len], PATH_MAX - len, "/%s", pathname);
    return report_access(syscallName, eventType, fullpath);
}

ssize_t BxlObserver::fd_to_path(int fd, char *buf, size_t bufsiz)
{
    char procPath[100] = {0};
    sprintf(procPath, "/proc/self/fd/%d", fd);
    ssize_t result = real_readlink(procPath, buf, bufsiz);
    LOG_DEBUG("<%d> --> %s", fd, buf);
    return result;
}

std::string BxlObserver::normalize_path_at(int dirfd, const char *pathname)
{
    char fullpath[PATH_MAX] = {0};
    char finalPath[PATH_MAX] = {0};
    ssize_t len = 0;

    const char *returnValue;

    // no pathname given --> read path for dirfd
    if (pathname == NULL)
    {
        fd_to_path(dirfd, fullpath, PATH_MAX);
        returnValue = fullpath;
    }
    // if relative path --> resolve it against dirfd
    else if (*pathname != '/' && *pathname != '~')
    {
        if (dirfd == AT_FDCWD)
        {
            getcwd(fullpath, PATH_MAX);
            len = strlen(fullpath);
        }
        else
        {
            len = fd_to_path(dirfd, fullpath, PATH_MAX);
        }

        if (len <= 0)
        {
            _fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
        }

        snprintf(&fullpath[len], PATH_MAX - len, "/%s", pathname);
        returnValue = fullpath;
    }
    else
    {
        returnValue = pathname;
    }

    LOG_DEBUG("<%d>%s --> %s", dirfd, pathname, returnValue);
    return returnValue;
}
