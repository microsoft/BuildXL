// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <string>
#include <string.h>
#include <unistd.h>
#include <sys/stat.h>

#include "coreruncommon.h"
#include "SystemExtensionManager.h"

int main(int argc, const char * argv[])
{
    @autoreleasepool
    {
        SystemExentsionAction action = None;

        for(int i = 1; i < argc; i++)
        {
            if (strcmp(argv[i], "--register-systemextension") == 0)
            {
                action = RegisterSystemExtension;
                break;
            }
            else if (strcmp(argv[i], "--unregister-systemextension") == 0)
            {
                action = UnregisterSystemExtension;
                break;
            }
            else if (strcmp(argv[i], "--test-xpcconnection") == 0)
            {
                action = TestXPCConnection;
                break;
            }
        }

        if (action != None)
        {
            SystemExtensionManager *extensionManger = [[SystemExtensionManager alloc] init];
            bool status = [extensionManger executeSystemExtensionOperationFor:action];
            exit(status ? EXIT_SUCCESS : EXIT_FAILURE);
        }
    }

    std::string argv0AbsolutePath;
    if (!GetAbsolutePath(argv[0], argv0AbsolutePath))
    {
        perror("Could not get full path to current application executable, exiting!");
        exit(EXIT_FAILURE);
    }

    // Get name of self and containing folder (typically the MacOS folder)
    int lastSlashPos = (int) argv0AbsolutePath.rfind('/');
    std::string appName = argv0AbsolutePath.substr(lastSlashPos + 1);
    std::string appFolder = argv0AbsolutePath.substr(0, lastSlashPos);

    // Strip off "MacOS" to get to the "Contents" folder
    std::string contentsFolder;
    if (!GetDirectory(appFolder.c_str(), contentsFolder))
    {
        perror("Could not find the embedded 'Contents' folder, exiting!");
        exit(EXIT_FAILURE);
    }

    // Append standard locations
    std::string clrFilesAbsolutePath = contentsFolder + "/CoreClrBundle";
    std::string managedFolderAbsolutePath = contentsFolder + "/ManagedBundle/";
    std::string managedAssemblyAbsolutePath = managedFolderAbsolutePath + "bxl.dll";

    // Pass all command line arguments to managed executable
    const char** managedAssemblyArgv = argv + 1;
    int managedAssemblyArgc = argc - 1;

    // Check if the specified managed assembly file exists
    struct stat sb;
    if (stat(managedAssemblyAbsolutePath.c_str(), &sb) == -1)
    {
        fprintf(stderr, "Could not find the main assembly (%s) to execute, exiting!\n", managedAssemblyAbsolutePath.c_str());
        exit(EXIT_FAILURE);
    }

    // Verify that the managed assembly path points to a file
    if (!S_ISREG(sb.st_mode))
    {
        fprintf(stderr, "The main assembly (%s) is not a valid file, exiting!\n", managedAssemblyAbsolutePath.c_str());
        exit(EXIT_FAILURE);
    }

    int exitCode = ExecuteManagedAssembly(
                            argv0AbsolutePath.c_str(),
                            clrFilesAbsolutePath.c_str(),
                            managedAssemblyAbsolutePath.c_str(),
                            managedAssemblyArgc,
                            managedAssemblyArgv);

    return exitCode;
}
