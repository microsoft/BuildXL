// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "PTraceSandbox.hpp"

bool verifyargs(BxlObserver *bxl, pid_t traceepid, pid_t parentpid, std::string exe, std::string mqname)
{
    bool valid = true;

    if (bxl == NULL)
    {
        return false;
    }

    if (traceepid < 0)
    {
        BXL_LOG_DEBUG(bxl, "[PTraceRunner] Invalid traceepid '%d' provided.", traceepid);
        valid = false;
    }

    if (parentpid < 0)
    {
        // Parent pid is not critical to running the sandbox, if it's not provided then it's not a big deal
        BXL_LOG_DEBUG(bxl, "[PTraceRunner] Invalid parentpid '%d' provided.", traceepid);
    }

    if (mqname.length() == 0 || mqname[0] != '/')
    {
        BXL_LOG_DEBUG(bxl, "[PTraceRunner] Invalid message queue name '%s'.", mqname.c_str());
        valid = false;
    }

    if (exe.length() == 0)
    {
        // exe is not critical to running the sandbox
        BXL_LOG_DEBUG(bxl, "[PTraceRunner] Invalid exe '%s'.", mqname.c_str());
    }

    return valid;
}

/**
 * The PTraceDaemon will launch this runner with a PID.
 * An instance of PTraceSandbox will then be created to trace the process tree starting from the root pid.
 */
int main(int argc, char **argv)
{
    int opt;
    pid_t traceepid;
    pid_t parentpid;
    std::string exe;
    std::string mq;
    
    // Parse arguments
    while((opt = getopt(argc, argv, "cpxm")) != -1)
    {
        switch (opt)
        {
            case 'c':
                // -c <pid of process to trace>
                traceepid = atoi(argv[optind]);
                break;
            case 'p':
                // -p <pid of parent process of process being traced>
                parentpid = atoi(argv[optind]);
                break;
            case 'x':
                // -x <path to statically linked executable>
                exe = std::string(argv[optind]);
                break;
            case 'm':
                // -m </mqname>
                mq = std::string(argv[optind]);
                break;
        }
    }

    BxlObserver *bxl = BxlObserver::GetInstance();
    PTraceSandbox sandbox(bxl);

    // FAM path will be verified by the BxlObserver constructor
    if (!verifyargs(bxl, traceepid, parentpid, exe, mq))
    {
        _exit(-1);
    }

    BXL_LOG_DEBUG(bxl, "[PTraceRunner:%d] Attaching to process %d", getpid(), traceepid);

    sandbox.AttachToProcess(traceepid, parentpid, exe, mq);

    _exit(0);
}