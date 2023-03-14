// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <cstdarg>
#include <execinfo.h>
#include <fcntl.h>
#include <iostream>
#include <limits.h>
#include <mqueue.h>
#include <signal.h>
#include <string.h>
#include <string>
#include <sys/wait.h>
#include <unistd.h>

#include "common.h"

#define LOG_MAX 4096

mqd_t mqdes;
std::string mqname;

enum loglevel
{
    debug,
    error
};

void log(loglevel ll, const char *msg, ...)
{
    char buffer[LOG_MAX];
    va_list args;

    va_start(args, msg);
    auto size = vsnprintf(buffer, LOG_MAX, msg, args);
    va_end(args);

    switch (ll)
    {
        case error:
            std::cerr << buffer << std::endl;
            break;
        case debug:
            std::cout << buffer << std::endl;
            break;
    }
}

// Handles SIGUSR1
void signalhandler(int signum)
{
    switch (signum)
    {
        case SIGSEGV:
        {
            // We should restore the default signal handler here first in case the code below also hits a SIGSEGV
            // SIG_DFL = default signal handler
            signal(signum, SIG_DFL);

            // Write stack trace to stderr on sigsegv
            fprintf(stderr, "Caught SIGSEGV. Stack trace:\n");
            void *array[10];
            size_t size = backtrace(array, 10);
            backtrace_symbols_fd(array, size, STDERR_FILENO);

            // raise the same signal again to get a coredump
            kill(getpid(), signum);
            break;
        }
        case SIGUSR1:
        {
            log(debug, "Received SIGUSR1 from BuildXL, shutting down.");
            mq_close(mqdes);
            mq_unlink(mqname.c_str());
            _exit(0);
            break;
        }
    }
}

/**
 * Create a substring from start to the provided delimiter and return the new start position not including the delimiter
 */
int getsubstring(std::string &source, int start, std::string &substring)
{
    size_t pos = source.find('|', start);
    if (pos != std::string::npos)
    {
        substring = source.substr(start, pos - start);
    }
    else
    {
        substring = source.substr(start, source.length() - start);
        pos = source.length();
    }

    return pos + 1;
}

std::string getenvvar(const char *name, const char *value)
{
    std::string result(name);
    result.append("=");
    result.append(value);

    return result;
}

bool openmq()
{
    struct mq_attr attr;

    // Create message queue for IPC between daemon and interpose sandbox
    attr.mq_flags = 0;
    attr.mq_maxmsg = 10;
    attr.mq_msgsize = PTRACED_MQ_MSG_SIZE;
    attr.mq_curmsgs = 0;

    mqdes = mq_open(mqname.c_str(), O_CREAT | O_RDONLY, 0644, &attr);

    if (mqdes == -1)
    {
        log(debug, "Failed open MQ with error: '%s', retrying.", strerror(errno));

        // a previously launched daemon might not have properly cleaned up here, lets close its message queue first
        mq_unlink(mqname.c_str());

        mqdes = mq_open(mqname.c_str(), O_CREAT | O_RDONLY, 0644, &attr);

        if (mqdes == -1)
        {
            // Unable to open the message queue, no choice but to exit here
            log(error, "Unable to open message queue '%s' with error '%s'", mqname.c_str(), strerror(errno));
            return false;
        }
    }

    return true;
}

/*
 * The PTrace daemon is responsible for listening to requests to launch the PTraceRunner to attach to a running process using PTRACE_ATTACH.
 */
int main(int argc, char **argv)
{
    int opt;
    char buffer[PTRACED_MQ_MSG_SIZE + 1];
    std::string ptracerunnerlocation;

    // Parse args
    while((opt = getopt(argc, argv, "mr")) != -1)
    {
        switch (opt)
        {
            case 'm':
                // -m <name of message queue>
                mqname = std::string(argv[optind]);
                break;
            case 'r':
                // -r </path/to/ptracerunner>
                ptracerunnerlocation = std::string(argv[optind]);
                break;
        }
    }

    log(debug, "Starting PTraceDaemon with mq: '%s', ptracerunner: '%s'", mqname.c_str(), ptracerunnerlocation.c_str());

    if (!openmq())
    {
        _exit(-1);
    }

    // Register signal handler to handle SIGUSR1 which will be sent from bxl when it wants this daemon to terminate
    signal(SIGUSR1, signalhandler);
    signal(SIGSEGV, signalhandler);

    while (true)
    {
        memset(buffer, 0, PTRACED_MQ_MSG_SIZE + 1);
        auto bytesread = mq_receive(mqdes, buffer, PTRACED_MQ_MSG_SIZE, /* msg_prio */ NULL);

        if (bytesread == -1)
        {
            // This happens if the mq handle is bad, it likely means that the queue was unlinked from outside of this daemon
            // This is okay because on the next build bxl will restart the daemon process to resolve this.
            if (errno == EBADF)
            {
                // mq was closed, don't need to do any clean up here
                _exit(-1);
            }
            else
            {
                break;
            }
        }

        buffer[bytesread] = '\0';
        log(debug, "Received ptrace request: '%s'", buffer);

        std::string command;
        std::string source(buffer);

        int start = getsubstring(source, 0, command);

        auto c = (ptracecommand)atoi(command.c_str());
        switch (c)
        {
            case run:
            {
                std::string traceepid;
                std::string parentpid;
                std::string exe;
                std::string fampath;

                // Message format: command|traceePid|parentPid|exe|famPath
                start = getsubstring(source, start, traceepid);
                start = getsubstring(source, start, parentpid);
                start = getsubstring(source, start, exe);
                start = getsubstring(source, start, fampath);

                // Launch a new child process to run the PTraceRunner
                auto child = fork();

                if (child == 0)
                {
                    char *childargv[] = 
                    {
                        "ptracerunner",
                        "-c",
                        (char *)traceepid.c_str(),
                        "-p",
                        (char *)parentpid.c_str(),
                        "-x",
                        (char *)exe.c_str(),
                        "-m",
                        (char *)mqname.c_str(),
                        NULL
                    };

                    auto mqvar = getenvvar(BxlPTraceMqName, mqname.c_str());
                    auto famvar = getenvvar(BxlEnvFamPath, fampath.c_str());

                    char *childenvp[] = 
                    {
                        (char *)mqvar.c_str(),
                        (char *)famvar.c_str(),
                        NULL
                    };

                    execve(ptracerunnerlocation.c_str(), childargv, childenvp);

                    log(error, "Failed to exec ptracerunner for request '%s'", buffer);
                    _exit(-1);
                }
                else if (child < 0)
                {
                    log(error, "Failed to spawn child process for request '%s'", buffer);
                    _exit(-1);
                }

                log(debug, "Spawned child process '%d' to trace '%s' with FAM '%s'", child, traceepid.c_str(), fampath.c_str());
                break;
            }
            case exitnotification:
            {
                // Message format: command|traceePid
                std::string traceepid;
                getsubstring(source, start, traceepid);
                pid_t pid = atoi(traceepid.c_str());

                // Collect child status to allow the system to release resources associated this with child
                int status;
                waitpid(pid, &status, 0);

                log(debug, "Received exit notification from '%d'", pid);
                break;
            }
        }
    }

    mq_close(mqdes);
    mq_unlink(mqname.c_str());

    return 0;
}