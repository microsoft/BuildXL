// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <iostream>
#include <string>
#include <fstream>
#include <stdio.h>
#include <unistd.h>
#include <limits.h>

#define STATICALLY_LINKED_PROCESS_NAME "TestProcessStaticallyLinked"

int main(int argc, char **argv)
{
    // Perform file access
    auto testFileName = "testFile.txt";

    std::ofstream fstream;
    fstream.open(testFileName);
    fstream << "TestFile.\n";
    fstream.close();

    // If requested, launch statically linked binary as sub process to verify whether file accesses are detected
    // When a process is launched with execv, argv[0] will not be set to the program name, instead it will be the first argument
    auto launchSubProcess = std::string(argc == 1 ? argv[0] : argv[1]).find("1") != std::string::npos;
    if (launchSubProcess)
    {
        char cwd[PATH_MAX];
        if (getcwd(cwd, sizeof(cwd)) == NULL)
        {
            std::cerr <<  "Unable to get current working directory" << std::endl;
            return 1;
        }

        std::string subProcessPath(cwd);
        subProcessPath.append("/");
        subProcessPath.append(STATICALLY_LINKED_PROCESS_NAME);

        std::cout << "Launching sub process '" << subProcessPath << "'" << std::endl;
        const char* arguments[] = {"0", nullptr};
        // Sub process should just perform some file accesses and exit without spawning another
        execv(subProcessPath.c_str(), const_cast<char* const*>(arguments));
    }

    return 0;
}