// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include "BuildXLSandboxShared.hpp"
#include "BuildXLException.hpp"
#include "DetoursSandbox.hpp"

DetoursSandbox::DetoursSandbox(pid_t host_pid, process_callback callback)
{
    assert(callback != nullptr);
    eventCallback_ = callback;
    
    hostPid_ = host_pid;
    
    char queueName[PATH_MAX] = { '\0' };
    sprintf(queueName, "com.microsoft.buildxl.socket.eventqueue_%d", host_pid);
    
    eventQueue_ = dispatch_queue_create(queueName, dispatch_queue_attr_make_with_qos_class(
        DISPATCH_QUEUE_SERIAL, QOS_CLASS_USER_INITIATED, -1
    ));
    
    if ((socketHandle_ = socket(AF_UNIX, SOCK_STREAM, 0)) < 0) {
        throw BuildXLException("Could not create socket for IPC interpose observation: " + std::to_string(errno));
    }

    int flags = fcntl(socketHandle_, F_GETFL) | O_NONBLOCK;
    if (fcntl(socketHandle_, F_SETFL, flags) < 0)
    {
        throw BuildXLException("Could not setup the socket for non-blocking operation: " + std::to_string(errno));
    }

    memset(&socketAddr_, 0, sizeof(socketAddr_));
    socketAddr_.sun_family = AF_UNIX;
    strncpy(socketAddr_.sun_path, socket_path, sizeof(socketAddr_.sun_path) - 1);
    unlink(socket_path);

    if (bind(socketHandle_, (struct sockaddr *)&socketAddr_, sizeof(socketAddr_)) < 0) {
        throw BuildXLException("Could not bind socket for IPC interpose observation: " + std::to_string(errno));
    }

    if (listen(socketHandle_, SOMAXCONN) < 0)
    {
        throw BuildXLException("Could not setup listen() to accept client connections: " + std::to_string(errno));
    }
    
    kqueueHandle_ = kqueue();
        
    dispatch_async(eventQueue_, ^
    {
        struct kevent eventSet;
        EV_SET(&eventSet, socketHandle_, EVFILT_READ, EV_ADD | EV_RECEIPT, 0, 0, NULL);
        assert(-1 != kevent(kqueueHandle_, &eventSet, 1, NULL, 0, NULL));
          
        struct kevent eventList[512];
        
        while (true)
        {
            int nev = kevent(kqueueHandle_, NULL, 0, eventList, sizeof(eventList), NULL);
            
            for (int i = 0; i < nev; i++)
            {
                int fd = (int)eventList[i].ident;
                
                if (eventList[i].flags & EV_EOF)
                {
                    close(fd);
                }
                else if (fd == socketHandle_)
                {
                    struct sockaddr_storage addr;
                    socklen_t socklen = sizeof(addr);
                
                    int connfd = accept(fd, (struct sockaddr *)&addr, &socklen);
                    assert(connfd != -1);
                    
                    EV_SET(&eventSet, connfd, EVFILT_READ, EV_ADD | EV_RECEIPT, 0, 0, NULL);
                    kevent(kqueueHandle_, &eventSet, 1, NULL, 0, NULL);
                    
                    int flags = fcntl(connfd, F_GETFL, 0);
                    assert(flags >= 0);
                    fcntl(connfd, F_SETFL, flags | O_NONBLOCK);
                }
                else if (eventList[i].filter == EVFILT_READ)
                {
                    char buffer[IOEvent::max_size()];
                    size_t bytes_read = recv(fd, buffer, IOEvent::max_size(), 0);
                    assert(bytes_read == IOEvent::max_size());
                    
                    imemorystream ims(buffer, bytes_read);
                    ims.imbue(std::locale(ims.getloc(), new PipeDelimiter));
                    IOEvent event;
                    ims >> event;
                    
                    log_debug("%{public}.*s",(int)bytes_read, buffer);
                    eventCallback_(nullptr, const_cast<const IOEvent &>(event), GetHostPid(), IOEventBacking::Interposing);
                }
            }
        }
    });
        
    log_debug("%s", "Successfully initialized the Detours sandbox backend.");
}

DetoursSandbox::~DetoursSandbox()
{
    shutdown(socketHandle_, SHUT_RDWR);
    unlink(socket_path);
    
    if (eventQueue_ != nullptr)
    {
        dispatch_release(eventQueue_);
    }
}
