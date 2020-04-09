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

extern const char *__progname;

static void HandleAccessReport(AccessReport report, int _)
{
    BxlObserver::GetInstance()->SendReport(report);
}

BxlObserver* BxlObserver::GetInstance()
{
    static BxlObserver s_singleton;
    return &s_singleton;
}

BxlObserver::BxlObserver()
{
    GEN_REAL(FILE*, fopen, const char *, const char *);
    GEN_REAL(size_t, fread, void*, size_t, size_t, FILE*);
    GEN_REAL(int, fclose, FILE*);
    GEN_REAL(ssize_t, readlink, const char *, char *, size_t);

    real_readlink("/proc/self/exe", progFullPath_, PATH_MAX);

    // read FAM env var
    const char *famPath = getenv(BxlEnvFamPath);
    if (!(famPath && *famPath))
    {
        _fatal("Env var '%s' not set", BxlEnvFamPath);
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

bool BxlObserver::Send(const char *buf, size_t bufsiz)
{
    GEN_REAL(int, open, const char *, int);
    GEN_REAL(ssize_t, write, int, const void*, size_t);
    GEN_REAL(int, close, int);

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
    int logFd = real_open(reportsPath, O_WRONLY | O_APPEND);
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
    GEN_REAL(char*, realpath, const char*, char*);

    // there is no central sendbox process here (i.e., there is an instance of this 
    // guy in every child process), so counting process tree size is not feasible
    if (report.operation == FileOperation::kOpProcessTreeCompleted)
    {
        return true;
    }

    char realpathBuf[PATH_MAX];
    char *realpathPtr = real_realpath(report.path, realpathBuf);

    int err                    = realpathPtr == NULL ? 2 : 0;
    const char *reportPath     = realpathPtr == NULL ? report.path : realpathPtr;
    RequestedAccess realAccess = realpathPtr == NULL ? RequestedAccess::Probe : (RequestedAccess)report.requestedAccess;

    const int PrefixLength = sizeof(uint);
    char buffer[PIPE_BUF] = {0};
    int maxMessageLength = PIPE_BUF - PrefixLength;
    int numWritten = snprintf(
        &buffer[PrefixLength], maxMessageLength, "%s|%d|%d|%d|%d|%d|%d|%s\n", 
        __progname, getpid(), (int)realAccess, report.status, report.reportExplicitly, err, report.operation, reportPath);
    if (numWritten == maxMessageLength)
    {
        // TODO: once 'send' is capable of sending more than PIPE_BUF at once, allocate a bigger buffer and send that
        _fatal("Message truncated to fit PIPE_BUF (%d): %s", PIPE_BUF, buffer);
    }

    *(uint*)(buffer) = numWritten;
    return Send(buffer, numWritten + PrefixLength);
}
