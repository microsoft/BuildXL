//
//  BuildXLSandboxCLI.c
//  MacSanboxCLI
//
//  Copyright © 2018 Microsoft. All rights reserved.
//

#include <IOKit/IOKitLib.h>
#include <IOKit/IODataQueueClient.h>
#include "BuildXLSandboxShared.hpp"

using namespace std;

kern_return_t MyUserClientOpenExample(io_service_t service, io_connect_t *connect)
{
    kern_return_t kernResult =
        IOServiceOpen(service, mach_task_self(), 0, connect);

    if (kernResult != KERN_SUCCESS)
    {
        fprintf(stderr, "IOServiceOpen returned 0x%08x\n", kernResult);
    }
    else
    {
        if (kernResult == KERN_SUCCESS)
        {
            printf("MyOpenUserClient was successful.\n\n");
        }
        else
        {
            fprintf(stderr, "MyOpenUserClient returned 0x%08x.\n\n", kernResult);
        }
    }

    return kernResult;
}


void MyUserClientCloseExample(io_connect_t connect)
{
    kern_return_t kernResult = IOServiceClose(connect);

    if (kernResult == KERN_SUCCESS)
    {
        printf("IOServiceClose was successful.\n\n");
    }
    else
    {
        fprintf(stderr, "IOServiceClose returned 0x%08x\n\n", kernResult);
    }
}

void TestUserClient(io_service_t service)
{
    kern_return_t kernResult;
    io_connect_t connect;

    kernResult = MyUserClientOpenExample(service, &connect);

    IpcData payload = {
        .pipId = (int64_t) getpid(),
        .processId = getpid() + 1,
        .clientPid = getpid(),
        .payloadLength = 0x4321,
        .action = kDominoSandboxActionSendPipStarted
    };

    char *buffer = (char *)malloc(payload.payloadLength);
    for (int i=0; i < payload.payloadLength; i++)
    {
        buffer[i] = 'a';
    }

    buffer[payload.payloadLength - 1] = '\0';

    payload.payloadAddress = (uintptr_t) buffer;

    kernResult = IOConnectCallMethod(connect, kIpcActionPipStateChanged, NULL, 0, &payload, sizeof(IpcData), NULL, NULL, NULL, NULL);
    if (kernResult == KERN_SUCCESS)
    {
        printf("MyScalarIStructureI was successful.\n\n");
    }
    else
    {
        fprintf(stderr, "MyScalarIStructureI returned 0x%08x.\n\n", kernResult);
    }

    free(buffer);

    kern_return_t result;

    // Allocate a mach port to receive notifactions from the IODataQueue
    mach_port_t port = IODataQueueAllocateNotificationPort();
    if (port == MACH_PORT_NULL)
    {
        printf("Couldn't allocate mach port");
        return;
    }

    result = IOConnectSetNotificationPort(connect, FileAccessReporting, port, 0);
    if (result != kIOReturnSuccess)
    {
        printf("Couldn't register notification port");
        mach_port_destroy(mach_task_self(), port);
        return;
    }

    mach_vm_size_t size = 0;
    mach_vm_address_t address = 0;

    result = IOConnectMapMemory(connect, FileAccessReporting, mach_task_self(), &address, &size, kIOMapAnywhere);
    if (result != kIOReturnSuccess)
    {
        printf("Couldn't get clientMemoryForType()");
        mach_port_destroy(mach_task_self(), port);
        return;
    }

    IODataQueueMemory *queue = (IODataQueueMemory *)address;

    printf("Processing items...\n");

    do
    {
        while (IODataQueueDataAvailable(queue))
        {
            AccessReport report;
            uint32_t reportSize = sizeof(report);

            result = IODataQueueDequeue(queue, &report, &reportSize);
            if (result == kIOReturnSuccess)
            {
                printf("Report for: PID(%d) PIP(%#llx)", report.pid, report.pipId);
            }
            else
            {
                printf("Error processing queue!");
                exit(2);
            }
        }
    }
    while (IODataQueueWaitForAvailableData(queue, port) == kIOReturnSuccess);

    IOConnectUnmapMemory(connect, FileAccessReporting, mach_task_self(), address);
    mach_port_destroy(mach_task_self(), port);

    if (connect != IO_OBJECT_NULL)
    {
        MyUserClientCloseExample(connect);
    }
}

kern_return_t MyCloseUserClient(io_connect_t connect)
{
    return KERN_SUCCESS;
}

int main(int argc, char *argv[])
{
    kern_return_t kernResult;
    io_service_t service;
    io_iterator_t iterator;
    bool driverFound = false;

    kernResult = IOServiceGetMatchingServices(kIOMasterPortDefault, IOServiceMatching(kDominoSandboxClassName), &iterator);
    if (kernResult != KERN_SUCCESS)
    {
        fprintf(stderr, "IOServiceGetMatchingServices returned 0x%08x\n\n", kernResult);
        return -1;
    }

    while ((service = IOIteratorNext(iterator)) != IO_OBJECT_NULL)
    {
        driverFound = true;
        TestUserClient(service);
        break;
    }

    IOObjectRelease(iterator);

    if (driverFound == false)
    {
        fprintf(stderr, "No matching drivers found.\n");
    }

    return EXIT_SUCCESS;
}
