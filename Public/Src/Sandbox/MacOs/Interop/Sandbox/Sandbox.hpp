// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Sandbox_h
#define Sandbox_h

#include "BuildXLException.hpp"
#include "Common.hpp"
#include "DetoursSandbox.hpp"
#include "EndpointSecuritySandbox.hpp"
#include "IOEvent.hpp"
#include "SandboxedPip.hpp"
#include "SandboxedProcess.hpp"
#include "Trie.hpp"

#include <signal.h>
#include <map>

#define SB_WRONG_BUFFER_SIZE    0x8
#define SB_INSTANCE_ERROR       0x16

enum Configuration
{
    EndpointSecuritySandboxType = 0,
    DetoursSandboxType,
    HybridSandboxType,
    DetoursLinuxSandboxType
};

typedef struct {
    Configuration config;
    int error;
} SandboxConnectionInfo;

extern "C"
{
    void InitializeSandbox(SandboxConnectionInfo *info, pid_t host_pid);
    void DeinitializeSandbox();

    void __cdecl ObserverFileAccessReports(SandboxConnectionInfo *info, AccessReportCallback callback, long accessReportSize);
};

bool Sandbox_SendPipStarted(const pid_t pid, pipid_t pipId, const char *const famBytes, int famBytesLength);
bool Sandbox_SendPipProcessTerminated(pipid_t pipId, pid_t pid);

class Sandbox final
{
    
private:
    
    pid_t hostPid_ = 0;
    
#if __APPLE__
    dispatch_queue_t hybird_event_queue_;
    xpc_connection_t xpc_bridge_ = nullptr;
    std::mutex access_mutex;
#endif
    
    std::map<pid_t, pid_t> whitelistedPids_;
    std::map<pid_t, pid_t> forceForkedPids_;
    
    Trie<SandboxedProcess> *trackedProcesses_ = nullptr;
    AccessReportCallback accessReportCallback_ = nullptr;
    
    DetoursSandbox* detours_ = nullptr;
    EndpointSecuritySandbox* es_ = nullptr;
    
    Configuration configuration_;
    
public:

    Sandbox() = delete;
    Sandbox(pid_t host_pid, Configuration config);
    ~Sandbox();
    
#if __APPLE__
    inline const bool IsRunningHybrid() const { return configuration_ == Configuration::HybridSandboxType; }
    inline const dispatch_queue_t GetHybridQueue() const { return hybird_event_queue_; }
#endif
    
    inline std::map<pid_t, pid_t>& GetWhitelistedPidMap() { return whitelistedPids_; }
    inline std::map<pid_t, pid_t>& GetForceForkedPidMap() { return forceForkedPids_; }
    
    inline const bool SetProcessPidPair(std::map<pid_t, pid_t>& map, pid_t pid, pid_t ppid)
    {
#if __APPLE__
        const std::lock_guard<std::mutex> lock(access_mutex);
#endif
        return map.emplace(pid, ppid).second;
    }
    
    inline const bool RemoveProcessPid(std::map<pid_t, pid_t>& map, pid_t pid)
    {
#if __APPLE__
        const std::lock_guard<std::mutex> lock(access_mutex);
#endif
        
        auto result = map.find(pid);
        if (result != map.end())
        {
            return (map.erase(pid) == 1);
        }
        
        return false;
    }
    
    inline const void SetAccessReportCallback(AccessReportCallback callback) { accessReportCallback_ = callback; }
    
    std::shared_ptr<SandboxedProcess> FindTrackedProcess(pid_t pid);
    bool TrackRootProcess(std::shared_ptr<SandboxedPip> pip);
    bool TrackChildProcess(pid_t childPid, const char* childExecutable, std::shared_ptr<SandboxedProcess> parentProcess);
    bool UntrackProcess(pid_t pid, std::shared_ptr<SandboxedProcess> process);
    
    void const SendAccessReport(AccessReport &report, std::shared_ptr<SandboxedPip> pip);
};

#endif /* Sandbox_h */
