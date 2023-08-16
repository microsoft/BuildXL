// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <errno.h>
#include <iostream>
#include <sys/mman.h>
#include <unistd.h>

int AnonymousFileTest()
{
    int fd = memfd_create("testFile", MFD_ALLOW_SEALING);
    if (fd == -1)
    {
        std::cerr << "memfd_create failed with errno " << errno << std::endl;
        return 2;
    }

    // Run a few system calls to see if any accesses to the anonymous file is reported to BuildXL
    if (ftruncate(fd, /* length */ 10) == -1)
    {
        std::cerr << "ftruncate failed with errno " << errno << std::endl;
        return 3;
    }

    if (close(fd) == -1)
    {
        std::cerr << "close failed with errno " << errno << std::endl;
        return 4;
    }

    return 0;
}

int main(int argc, char **argv)
{
    int opt;
    std::string testName;
    
    // Parse arguments
    while((opt = getopt(argc, argv, "t")) != -1)
    {
        switch (opt)
        {
            case 't':
                // -t <name of test to run>
                testName = std::string(argv[optind]);
                break;
        }
    }

    #define IF_COMMAND(NAME)   { if (testName == #NAME) { exit(NAME()); } }
    // Function Definitions
    IF_COMMAND(AnonymousFileTest);

    // Invalid command
    exit(-1);
}