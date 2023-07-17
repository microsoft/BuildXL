// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "PTraceSandbox.hpp"

bool verifyargs(BxlObserver *bxl, pid_t traceepid, std::string exe)
{
    bool valid = true;

    if (bxl == NULL)
    {
        return false;
    }

    if (traceepid < 0)
    {
        BXL_LOG_DEBUG(bxl, "Invalid traceepid '%d' provided.", traceepid);
        valid = false;
    }

    if (exe.length() == 0)
    {
        // exe is not critical to running the sandbox
        BXL_LOG_DEBUG(bxl, "Invalid exe '%s'.", exe.c_str());
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
    std::string exe;
    std::string semaphoreName = "/";
    
    // Parse arguments
    while((opt = getopt(argc, argv, "cx")) != -1)
    {
        switch (opt)
        {
            case 'c':
                // -c <pid of process to trace>
                traceepid = atoi(argv[optind]);
                break;
            case 'x':
                // -x <path to statically linked executable>
                exe = std::string(argv[optind]);
                break;
        }
    }

    BxlObserver *bxl = BxlObserver::GetInstance();
    PTraceSandbox sandbox(bxl);

    // FAM path will be verified by the BxlObserver constructor
    if (!verifyargs(bxl, traceepid, exe))
    {
        std::cerr << "Verify args failed failed: " << strerror(errno) << std::endl;
        _exit(-1);
    }

    if (getenv("__BUILDXL_TEST_PTRACERUNNER_FAILME")) // CODESYNC: PTraceSandboxedProcessTest 
    {
        std::cerr << "Intentionally erroring for that one particular test.";
        _exit(-10);
    }

    semaphoreName.append(std::to_string(traceepid));

    sandbox.AttachToProcess(traceepid, exe, semaphoreName);

    _exit(0);
}