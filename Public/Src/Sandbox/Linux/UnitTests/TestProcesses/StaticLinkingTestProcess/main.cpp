// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <iostream>
#include <string>
#include <fstream>
#include <stdio.h>
#include <unistd.h>
#include <limits.h>
#include <sys/stat.h>
#include <fcntl.h>

#define STATICALLY_LINKED_PROCESS_NAME "TestProcessStaticallyLinked"

// Appends filename to a provided root path
std::string GetPath(std::string root, std::string filename)
{
    std::string path(root);
    path.append("/");
    path.append(filename);

    return path;
}

int main(int argc, char **argv)
{
    // Perform file access
    auto testFileName = "testFile.txt";

    std::ofstream fstream;
    fstream.open(testFileName);
    fstream << "TestFile.\n";
    fstream.close();

    // CODESYNC: Public/Src/Engine/UnitTests/Processes/SandboxedProcessTest.cs
#ifdef STATICALLYLINKED
    std::cout << "STATIC" << std::endl;
    char cwd[PATH_MAX];
    getcwd(cwd, sizeof(cwd));
    std::string workingDir(cwd);

    unlink(GetPath(workingDir, "unlinkme").c_str());

    struct stat statbuf;
    auto res = stat(GetPath(workingDir, "writeme").c_str(), &statbuf);

    int fd = open(GetPath(workingDir, "writeme").c_str(), O_CREAT);
    write(fd, workingDir.c_str(), workingDir.length());
    close(fd);

    rmdir(GetPath(workingDir, "rmdirme").c_str());

    rename(GetPath(workingDir, "renameme").c_str(), GetPath(workingDir, "renamed").c_str());
#endif

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