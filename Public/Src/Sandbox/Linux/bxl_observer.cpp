// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <algorithm>
#include <stack>
#include <sys/prctl.h>
#include <sys/wait.h>

#include "AccessChecker.h"
#include "bxl_observer.hpp"

AccessCheckResult BxlObserver::sNotChecked = AccessCheckResult::Invalid();

BxlObserver* BxlObserver::GetInstance()
{
    static BxlObserver s_singleton;
    return &s_singleton;
}

BxlObserver::BxlObserver()
{
    // These environment variables are set by BuildXL if ptrace is in use because the tracer runs in a separate process.
    const char *ptracePid = getenv(BxlPTraceTracedPid);
    bool isPTrace = !is_null_or_empty(ptracePid);

    if (isPTrace)
    {
        const char *ptracePath = getenv(BxlPTraceTracedPath);
        strlcpy(progFullPath_, ptracePath, PATH_MAX);
    }
    else
    {
        internal_readlink("/proc/self/exe", progFullPath_, PATH_MAX);
    }

    disposed_ = false;
    const char *rootPidStr = isPTrace ? ptracePid : getenv(BxlEnvRootPid);
    rootPid_ = is_null_or_empty(rootPidStr) ? -1 : atoi(rootPidStr);
    // value of "1" -> special case, set by BuildXL for the root process
    if (rootPid_ == 1) {
        rootPid_ = getpid();
    }

    InitFam(isPTrace ? rootPid_ : getpid());
    InitDetoursLibPath();

    const char* const forcedprocesses = getenv(BxlPTraceForcedProcessNames);
    if (!is_null_or_empty(forcedprocesses))
    {
        strlcpy(forcedPTraceProcessNamesList_, forcedprocesses, PATH_MAX);

        // Get the list consuming the semicolon-separated value
        const char* start = forcedprocesses; 
        const char* end = forcedprocesses; 
        while (*end != '\0') 
        {
            if (*end == ';') 
            {
                forcedPTraceProcessNames_.emplace_back(start, end - start);
                start = end + 1;
            }

            end++;
        }
        
        forcedPTraceProcessNames_.emplace_back(start, end - start);
    }

    // FAM must be initialized before the report path can be obtained
    if (CheckEnableLinuxPTraceSandbox(fam_->GetExtraFlags()))
    {
        strlcpy(secondaryReportPath_, GetReportsPath(), PATH_MAX);
        auto reportLength = strnlen(secondaryReportPath_, PATH_MAX);

        // CODESYNC: Public/Src/Engine/Processes/SandboxConnectionLinuxDetours.cs
        secondaryReportPath_[reportLength] = '2';
        secondaryReportPath_[reportLength + 1] = '\0';
    }
}

BxlObserver::~BxlObserver()
{
    if (messageCountingSemaphore_ != nullptr)
    {
        // best effort, no need to observe the return value here
        // if this does fail for whatever reason, the managed side should still unlink this semaphore
        internal_sem_close(messageCountingSemaphore_);
        messageCountingSemaphore_ = nullptr;
    }

    disposed_ = true;
}

void BxlObserver::InitDetoursLibPath()
{
    const char *path = getenv(BxlEnvDetoursPath);
    if (!is_null_or_empty(path))
    {
        strlcpy(detoursLibFullPath_, path, PATH_MAX);
    }
    else
    {
        detoursLibFullPath_[0] = '\0';
    }
}

void BxlObserver::InitFam(pid_t pid)
{
    // read FAM env var
    const char *famPath = getenv(BxlEnvFamPath);
    if (is_null_or_empty(famPath))
    {
        // This environment variable is set by the sandbox before calling exec
        // We always expect to have it on initialization of the observer
        _fatal_undefined_env(BxlEnvFamPath);
    }

    // Store the value for future uses, as the environment might be cleared by the running process
    strlcpy(famPath_, famPath, PATH_MAX);

    // read FAM
    FILE *famFile = internal_fopen(famPath_, "rb");
    if (!famFile)
    {
        _fatal("Could not open file '%s'; errno: %d", famPath_, errno);
    }

    fseek(famFile, 0, SEEK_END);
    long fam_length = ftell(famFile);
    rewind(famFile);

    // The FileAccessManifest object takes ownership of this payload and will handle deleting it
    auto fam_payload = new char [fam_length];
    internal_fread(fam_payload, fam_length, 1, famFile);
    internal_fclose(famFile);

    fam_ = new buildxl::common::FileAccessManifest(fam_payload, fam_length);

    sandboxLoggingEnabled_ = CheckEnableLinuxSandboxLogging(fam_->GetExtraFlags());
}

void BxlObserver::Init()
{
    // TODO [pgunasekara]: this piece can be moved into the constructor once the interpose library is removed.
    // If message counting is enabled, open the associated semaphore (this should already be created by the managed side)
    if (CheckCheckDetoursMessageCount(fam_->GetFlags()))
    {
        // Setting initializingSemaphore_ will communicate to the interpose layer to not interpose any libc functions called inside sem_open
        initializingSemaphore_ = true;
        messageCountingSemaphore_ = internal_sem_open(fam_->GetInternalErrorDumpLocation(), O_CREAT, 0644, 0);

        if (messageCountingSemaphore_ == SEM_FAILED)
        {
            // we'll log a message here, but this won't fail the pip until this feature is tested more thorougly
            internal_fprintf(stdout, "BuildXL injected message: File access monitoring failed to open message counting semaphore '%s' with errno: '%d'. You should rerun this build, or contact the BuildXL team if the issue persists across multiple builds.", fam_->GetInternalErrorDumpLocation(), errno);
        }

        initializingSemaphore_ = false;
    }

    bxlObserverInitialized_= true;
}

// Access Reporting
AccessCheckResult BxlObserver::CreateAccess(buildxl::linux::SandboxEvent& event, bool check_cache, bool basedOnPolicy) {
    if (!event.IsValid()) {
        LOG_DEBUG("Won't report an access for syscall %s because the event is invalid.", event.DebugGetSystemCall()); 
        return sNotChecked;
    }

    // Get mode if not already set by caller
    // Resolve paths and mode
    bool isFileEvent = ResolveEventPaths(event);

    // Check if non-file, we don't want to report these.
    if (!isFileEvent) {
        LOG_DEBUG("Won't report an access for syscall %s because the paths for the event couldn't be resolved. Path type: %d. Path resolution %d, Path %s", event.DebugGetSystemCall(), event.GetPathType(), event.GetRequiredPathResolution(), event.GetSrcPath().c_str()); 
        return sNotChecked;
    }
    
    // Check cache and return early if this access has already been checked.
    if (check_cache && IsCacheHit(event.GetEventType(), event.GetSrcPath(), event.GetDstPath())) {
        return sNotChecked;
    }

    // Perform access check
    auto result = sNotChecked;
    auto access_should_be_blocked = false;

    if (IsValid()) {
        result = buildxl::linux::AccessChecker::CheckAccessAndGetReport(fam_, event, basedOnPolicy);
        access_should_be_blocked = result.ShouldDenyAccess() && IsFailingUnexpectedAccesses();

        if (!access_should_be_blocked) {
            // This access won't be blocked, so let's cache it.
            // We cache event types that are always a miss in IsCacheHit, but this also should be fine.
            CheckCache(event.GetEventType(), event.GetSrcPath(), /* addEntryIfMissing */ true);
        }
    }
    else {
        // The caller of this function may have already set an access check result
        // However, this process is a breakaway process, therefore we don't want to report these accesses, so we'll update the access check here.
        event.SetSourceAccessCheck(result);
        event.SetDestinationAccessCheck(result);
    }

    // After resolving the paths and completing the access check we should freeze the event: this is to ensure consistency between the AccessCheckResult that we 
    // return here and the contents of this event.
    event.Seal();

    // Log path
    LOG_DEBUG("(( %10s:%2d )) %s %s%s", event.DebugGetSystemCall(), event.GetEventType(), event.GetSrcPath().c_str(),
        !result.ShouldReport() ? "[Ignored]" : result.ShouldDenyAccess() ? "[Denied]" : "[Allowed]",
        access_should_be_blocked ? "[Blocked]" : "");

    return result;
}

void BxlObserver::ReportAccess(buildxl::linux::SandboxEvent& event) {
    SendReport(event);
}

void BxlObserver::CreateAndReportAccess(buildxl::linux::SandboxEvent& event, bool check_cache, bool basedOnlyOnPolicy) {
    CreateAccess(event, check_cache, basedOnlyOnPolicy);
    ReportAccess(event);
}

// This method:
//  1. Normalizes the paths of an event, effectively turning the event to one with path type kAbsolutePaths,
//     except in the case where the file descriptor does not refer to a file (so absolute paths would be semantically incorrect).
//  2. Resolves and sets the mode of the source path for the event. 
//     We need to do this along with path resolution because we need to trace how the path was originally specified:
//     it is important to resolve the mode using the file descriptor if the original system call used one,
//     because the resolved absolute path might be ficticious (for example, for a file descriptor that 
//     is a socket, the 'absolute path' looks something like 'socket:[12345]', and get_mode on that path
//     returns an incorrect result). Note that after this function the event path type collapses to 
//     'kAbsolutePaths', as mentioned above, so that fact would be lost.
bool BxlObserver::ResolveEventPaths(buildxl::linux::SandboxEvent& event) {
    auto pathType = event.GetPathType();
    switch (pathType) {
        case buildxl::linux::SandboxEventPathType::kFileDescriptors: {
            // Update the mode using the file descriptor before resolving any paths
            if (event.GetMode() == 0) {
                event.SetMode(get_mode(event.GetSrcFd()));
            }

            if (is_non_file(event.GetMode())) {
                // don't bother normalizing the paths: making the event an absolute path event would be wrong here
                return false;
            }

            char resolved_path_src[PATH_MAX] = { 0 };
            char resolved_path_dst[PATH_MAX] = { 0 };

            if (event.GetSrcFd() != -1) {
                FileDescriptorToPath(event.GetSrcFd(), event.GetPid(), resolved_path_src, PATH_MAX);
            }

            if (event.GetDstFd() != -1) {
                FileDescriptorToPath(event.GetDstFd(), event.GetPid(), resolved_path_dst, PATH_MAX);
            }

            // FileDescriptorToPath returns a fully resolved path (by virtue of resolving /proc/self/fd/{fd})
            // so no need to call ResolveEventPaths here, we can just set them directly
            event.SetResolvedPaths(resolved_path_src, resolved_path_dst);
            break;
        }
        case buildxl::linux::SandboxEventPathType::kRelativePaths: {
            char resolved_path_src[PATH_MAX] = { 0 };
            char resolved_path_dst[PATH_MAX] = { 0 };

            if (event.GetSrcFd() != -1) {
                relative_to_absolute(event.GetSrcPath().c_str(), event.GetSrcFd(), event.GetPid(), resolved_path_src);
            }

            if (event.GetDstFd() != -1) {
                relative_to_absolute(event.GetDstPath().c_str(), event.GetDstFd(), event.GetPid(), resolved_path_dst);
            }

            ResolveEventPaths(event, resolved_path_src, resolved_path_dst);

            // Update the mode after normalization, so we use an absolute path for it
            if (event.GetMode() == 0) {
                event.SetMode(get_mode(event.GetSrcPath().c_str()));
            }
            break;
        } 
        case buildxl::linux::SandboxEventPathType::kAbsolutePaths: {
            // Paths already resolved but need to be normalized
            char resolved_path_src[PATH_MAX] = { 0 };
            char resolved_path_dst[PATH_MAX] = { 0 };

            strncpy(resolved_path_src, event.GetSrcPath().c_str(), PATH_MAX);
            strncpy(resolved_path_dst, event.GetDstPath().c_str(), PATH_MAX);

            ResolveEventPaths(event, resolved_path_src, resolved_path_dst);          

            // Update the mode after normalization
            if (event.GetMode() == 0) {
                event.SetMode(get_mode(event.GetSrcPath().c_str()));
            }
            break;
        }
        default:
            break;
    }

    if (is_non_file(event.GetMode())) {
        return false;
    }

    // After normalization, we should have valid absolute paths. If not, the file descriptor or paths were not associated to files to begin with, and we shouldn't proceed with the report
    if (event.GetSrcPath().empty()) {
        LOG_DEBUG("[ResolveEventPaths] Empty src path after normalization. Original event had path type %d", pathType);
        return false;
    }
    else if (event.GetSrcPath()[0] != '/') {
        LOG_DEBUG("[ResolveEventPaths] Non-absolute src path '%s' after normalization. Original event had path type %d", event.GetSrcPath().c_str(), pathType);
        return false;
    }

    if (!event.GetDstPath().empty() && event.GetDstPath()[0] != '/') {
        LOG_DEBUG("[ResolveEventPaths] Non-absolute dst path '%s' after normalization. Original event had path type %d", event.GetDstPath().c_str(), pathType);
        return false;
    }

    return true;
}

void BxlObserver::ResolveEventPaths(buildxl::linux::SandboxEvent& event, char *src_path, char *dst_path) {
    // Normalization might be disabled for internal events, or if paths have been already resolved
    auto requiredResolution = event.GetRequiredPathResolution();
    if (requiredResolution != buildxl::linux::RequiredPathResolution::kDoNotResolve) {
        bool follow_symlink = (requiredResolution == buildxl::linux::RequiredPathResolution::kFullyResolve);
        resolve_path(src_path, follow_symlink, event.GetPid(), event.GetParentPid());

        if (!event.GetDstPath().empty()) {
            resolve_path(dst_path, follow_symlink, event.GetPid(), event.GetParentPid());
        }

        event.SetResolvedPaths(src_path, dst_path);
    }
}

// TODO [pgunasekara]: This function duplicates the functionality of another function in this class that will be cleaned up in a future change.
void BxlObserver::FileDescriptorToPath(int fd, pid_t pid, char *out_path_buffer, size_t buffer_size) {
    if (fd < 0) {
        return;
    }

    if (fd >= MAX_FD) {
        read_path_for_fd(fd, out_path_buffer, buffer_size, pid);
        return;
    }

    if (useFdTable_) {
        // check the file descriptor table
        if (fdTable_[fd].length() > 0) {
            strncpy(out_path_buffer, fdTable_[fd].c_str(), buffer_size);
            return;
        }
    }

    // read from the filesystem and update the file descriptor table
    auto result = read_path_for_fd(fd, out_path_buffer, buffer_size, pid);
    if (result != -1) {
        // Only cache if read_path_for_fd succeeded.
        if (useFdTable_) {
            fdTable_[fd] = out_path_buffer;
        }
    }
}

bool BxlObserver::SendReport(buildxl::linux::SandboxEvent &event, bool use_secondary_pipe) {
    return SendReport(event, event.GetSourceAccessReport(), use_secondary_pipe)
        && SendReport(event, event.GetDestinationAccessReport(), use_secondary_pipe);
}

bool BxlObserver::SendReport(buildxl::linux::SandboxEvent &event, buildxl::linux::AccessReport report, bool use_secondary_pipe) {
    if (!event.IsValid()) {
        LOG_DEBUG("Won't send an access for syscall %s because the event is invalid.", event.DebugGetSystemCall()); 
        return true;
    }
    
    if (report.access_check_result.ShouldReport()) {
        char buffer[PIPE_BUF] = {0};
        unsigned int report_size = 0;

        bool success = buildxl::linux::ReportBuilder::SandboxEventReportString(
            event,
            report,
            buffer,
            PIPE_BUF,
            report_size);

        if (!success) {
            // TODO: once 'send' is capable of sending more than PIPE_BUF at once, allocate a bigger buffer and send that
            _fatal("Message truncated to fit (%d) bytes: %s. Path '%s'", PIPE_BUF, buffer, report.path.c_str());
        }

        // CODESYNC: Public/Src/Engine/Processes/SandboxedProcessUnix.cs
        bool shouldCountReportType = event.GetEventType() != buildxl::linux::EventType::kClone
            && event.GetEventType() != buildxl::linux::EventType::kExec
            && event.GetEventType() != buildxl::linux::EventType::kExit;

        return Send(buffer, report_size, use_secondary_pipe, /* countReport */ shouldCountReportType);
    }

    return true;
}

void BxlObserver::LogDebug(pid_t pid, const char *fmt, ...) {
    if (LogDebugEnabled()) {
        va_list args;
        va_start(args, fmt);
        LogDebugMessage(pid, buildxl::linux::DebugEventSeverity::kInfo, fmt, args);
        va_end(args);
    }
}

void BxlObserver::LogError(pid_t pid, const char *fmt, ...) {
    va_list args;
    va_start(args, fmt);
    LogDebugMessage(pid, buildxl::linux::DebugEventSeverity::kError, fmt, args);
    va_end(args);
}

void BxlObserver::LogDebugMessage(pid_t pid, buildxl::linux::DebugEventSeverity severity, const char *fmt, va_list args) {
    char message[PIPE_BUF] = { 0 };
    char report[PIPE_BUF] = { 0 };

    int numWritten = vsnprintf(message, PIPE_BUF, fmt, args);

    // Sanitize the debug message so we don't confuse the parser on managed code:
    // Pipes (|) are used to delimit the message parts and we expect one line (\n) per report, so
    // replace those occurrences with something else.
    for (int i = 0 ; i < PIPE_BUF; i++)
    {
        if (message[i] == '|')
        {
            message[i] = '!';
        }
        
        if (message[i] == '\n' || message[i] == '\r')
        {
            message[i] = '.';
        }
    }

    // Get report string
    int size = buildxl::linux::ReportBuilder::DebugReportReportString(severity, pid, message, report, PIPE_BUF);

    Send(report, size, /* useSecondaryPipe */ false, /* countReport */ false);
}

// Checks whether cache contains (event, path) pair and returns the result of this check.
// If the pair is not in cache and addEntryIfMissing is true, attempts to add the pair to cache.
bool BxlObserver::CheckCache(buildxl::linux::EventType event, const string &path, bool addEntryIfMissing)
{
    // This code could possibly be executing from an interrupt routine or from who knows where,
    // so to avoid deadlocks it's essential to never block here indefinitely.
    if (!cacheMtx_.try_lock_for(chrono::milliseconds(1)))
    {
        return false; // failed to acquire mutex -> forget about it
    }

    // ============================== in the critical section ================================

    // make sure the mutex is released by the end
    shared_ptr<timed_mutex> sp(&cacheMtx_, [](timed_mutex *mtx) { mtx->unlock(); });

    auto it = cache_.find(event);
    if (it == cache_.end())
    {
        if (addEntryIfMissing) 
        {
            unordered_set<string> set;
            set.insert(path);
            cache_.insert(make_pair(event, set));
        }

        return false;
    }

    if (addEntryIfMissing) 
    {
        return !it->second.insert(path).second;
    }
    else 
    {
        return (it->second.find(path) != it->second.end());
    }
}

bool BxlObserver::IsCacheHit(buildxl::linux::EventType event, const string &path, const string &secondPath)
{
    // (1) IMPORTANT           : never do any of this stuff after this object has been disposed!
    //     WHY                 : because the cache date structure is invalid at that point.
    //     HOW CAN THIS HAPPEN : we may get called from "on_exit" handlers, at which point the
    //                           global BxlObserver singleton instance can already be disposed.
    // (2) never cache FORK, EXEC, EXIT and events that take 2 paths
    if (disposed_ ||
        secondPath.length() > 0 ||
        event == buildxl::linux::EventType::kClone ||
        event == buildxl::linux::EventType::kExec ||
        event == buildxl::linux::EventType::kExit)
    {
        return false;
    }

    return CheckCache(event, path, /* addEntryIfMissing */ false);
}

bool BxlObserver::Send(const char *buf, size_t bufsiz, bool useSecondaryPipe, bool countReport)
{
    if (!real_open)
    {
        _fatal("syscall 'open' not found; errno: %d", errno);
    }

    // TODO: instead of failing, implement a critical section
    if (bufsiz > PIPE_BUF)
    {
        _fatal("Cannot atomically send a buffer whose size (%ld) is greater than PIPE_BUF (%d)", bufsiz, PIPE_BUF);
    }

    const char *reportsPath = useSecondaryPipe ? GetSecondaryReportsPath() : GetReportsPath();
    int logFd = real_open(reportsPath, O_WRONLY | O_APPEND, 0);
    if (logFd == -1)
    {
        _fatal("Could not open file '%s'; errno: %d", reportsPath, errno);
    }

    // update message counting semaphore whenever a report is sent
    // We update the message counting semaphore before sending the report because we could hit a race condition where
    // the message is received by the managed side but we haven't yet incremented the counter if we do it after sending the message.
    // If the message fails to send, the code below will write to stderr and exit with a bad exit code causing the pip to fail anyways.
    // So it doesn't matter if we increment the counter but fail to send a message.
    if (messageCountingSemaphore_ != nullptr && countReport)
    {
        auto result = real_sem_post(messageCountingSemaphore_);
        if (result != 0)
        {
            // something went wrong with the semaphore, we shouldn't call LOG_DEBUG here because it will just come back to this function
            // we also don't want to call _fatal because that will fail the pip.
            // instead log the error to stdout (this could be promoted to stderr in the future when this feature is stable)
            real_fprintf(stdout, "posting to buildxl message counting semaphore failed with errno: %d\n", errno);
        }
    }

    ssize_t numWritten = real_write(logFd, buf, bufsiz);
    if (numWritten < bufsiz)
    {
        _fatal("Wrote only %ld bytes out of %ld", numWritten, bufsiz);
    }

    // A handle was opened for our own internal purposes. That
    // could have reused a fd where we missed a close, 
    // so reset that entry in the fd table
    reset_fd_table_entry(logFd);

    real_close(logFd);

    return true;
}

bool BxlObserver::SendExitReport(pid_t pid, pid_t ppid)
{
    auto event = buildxl::linux::SandboxEvent::ExitSandboxEvent("exit", GetProgramPath(), pid, ppid);
    event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::Read, ResultAction::Allow, ReportLevel::Report));

    return SendReport(event);
}

std::string BxlObserver::GetProcessCommandLine(const char * const argv[]) {
    if (!IsReportingProcessArgs()) {
        return "";
    }
    
    return GetCommandLineFromArgv(argv);
}

std::string BxlObserver::GetProcessCommandLine(pid_t pid) {
    if (!IsReportingProcessArgs()) {
        return "";
    }

    return DoGetProcessCommandLine(pid);
}

std::string BxlObserver::DoGetProcessCommandLine(pid_t pid) {
    char path[PATH_MAX] = { 0 };
    int max_size = PIPE_BUF + sizeof(uint) - 1;
    char cmd_line_buffer[max_size] = { 0 };
    std::string cmd_line;
    bool first_arg = true;

    // /proc/<pid>/cmdline has a set of arguments separated by the null terminator
    snprintf(path, PATH_MAX, "/proc/%d/cmdline", pid);

    int fd = open(path, O_RDONLY);
    int bytes_read = read(fd, cmd_line_buffer, max_size);
    char *end = cmd_line_buffer + bytes_read;

    for (char *current_arg = cmd_line_buffer; current_arg < end; ) {
        if (first_arg) {
            first_arg = false;
        }
        else {
            cmd_line.append(" ");
        }

        cmd_line.append(current_arg);

        // Increment current_arg until the next null character is reached
        while(*current_arg++);
    }

    close(fd);

    return cmd_line;
}

bool BxlObserver::is_non_file(const mode_t mode)
{
    // Observe we don't care about block devices here. It is unlikely that we'll support them e2e, this is just an FYI.
    return mode != 0 && !S_ISDIR(mode) && !S_ISREG(mode) && !S_ISLNK(mode);
}

void BxlObserver::create_firstAllowWriteCheck(const char *full_path, int path_mode, int pid, int ppid, buildxl::linux::SandboxEvent& firstAllowWriteEvent)
{
    mode_t mode = path_mode == -1 ? get_mode(full_path) : path_mode;
    bool file_exists = mode != 0 && !S_ISDIR(mode);
    AccessCheckResult access_check(RequestedAccess::Write, file_exists ? ResultAction::Deny : ResultAction::Allow, ReportLevel::Report);
    firstAllowWriteEvent = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
        /* system_call */   "firstAllowWriteCheckInProcess",
        /* event_type */    buildxl::linux::EventType::kFirstAllowWriteCheckInProcess,
        /* pid */           pid == -1 ? getpid() : pid,
        /* ppid */          ppid == -1 ? getppid() : ppid,
        /* error */         0,
        /* src_path */      full_path);

    firstAllowWriteEvent.SetMode(mode);
    firstAllowWriteEvent.SetSourceAccessCheck(access_check);
}

void BxlObserver::report_firstAllowWriteCheck(const char *full_path, int path_mode, int pid, int ppid)
{
    buildxl::linux::SandboxEvent event;
    create_firstAllowWriteCheck(full_path, -1, -1, -1, event);

    SendReport(event);
}

bool BxlObserver::check_and_report_process_requires_ptrace(int fd)
{
    return check_and_report_process_requires_ptrace(fd_to_path(fd).c_str());
}

bool BxlObserver::IsPTraceForced(const char *path)
{
    // 1. Get the last component of the path (i.e., the program name)
    if (forcedPTraceProcessNames_.size() == 0) 
    {
        return false;
    }
    
    char *progname = basename((char*)path);
    return std::find(forcedPTraceProcessNames_.begin(), forcedPTraceProcessNames_.end(), std::string(progname)) != forcedPTraceProcessNames_.end();
}

bool BxlObserver::check_and_report_process_requires_ptrace(const char *path)
{
    if (!CheckEnableLinuxPTraceSandbox(fam_->GetExtraFlags()))
    {
        return false;
    }

    if (IsPTraceForced(path) || CheckUnconditionallyEnableLinuxPTraceSandbox(fam_->GetExtraFlags()))
    {
        // Allow this process to be traced by the tracer process.
        set_ptrace_permissions();

        // We force ptrace for this process. 
        // Send a "process requires ptrace" report so that the managed side can track it.
        auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   "ptrace",
            /* event_type */    buildxl::linux::EventType::kPTrace,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* error */         0,
            /* src_path */      path);
        event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report));
        SendReport(event, /* use_secondary_pipe */ true);

        return true;
    }

    // Stat the path to get the last modified time of the path
    // We need to do this because the executable file could be changed in between this stat and the previous stat
    // If it was changed (has a different modified time), then we should run the check on it once more
    struct stat statbuf;
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
        internal___lxstat(1, path, &statbuf);
#else
        internal_lstat(path, &statbuf);
#endif

    std::string key = std::to_string(statbuf.st_mtim.tv_sec);
    key.append(":");
    key.append(path);

    auto maybeProcess = std::find_if(
        ptraceRequiredProcessCache_.begin(),
        ptraceRequiredProcessCache_.end(),
        [key](const std::pair<std::string, bool>& item) { return item.first == key; }
    );

    bool requiresPtrace;
    if (maybeProcess != ptraceRequiredProcessCache_.end())
    {
        // Already checked this process
        requiresPtrace = maybeProcess->second;
    }
    else
    {
        requiresPtrace = is_statically_linked(path) || contains_capabilities(path);
        ptraceRequiredProcessCache_.push_back(std::make_pair(key, requiresPtrace));
    }

    if (requiresPtrace)
    {
        // Allow this process to be traced by the daemon process
        set_ptrace_permissions();

        auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   "ptrace",
            /* event_type */    buildxl::linux::EventType::kPTrace,
            /* pid */           getpid(),
            /* ppid */          getppid(),
            /* error */         0,
            /* src_path */      path);
        event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report));
        SendReport(event, /* use_secondary_pipe */ true);
    }

    return requiresPtrace;
}

void BxlObserver::set_ptrace_permissions()
{
    // This should happen before sending a kOpProcessRequiresPtrace report to bxl because it will signal bxl to launch the tracer.
    if (prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY) == -1)
    {
        std::cerr << "[BuildXL] Failed to allow ptrace for process " << getpid() << ": " << strerror(errno) << "\n";
        // This process is going to fail anyways when the tracer fails to attach, so we should exit here with a bad exit code.
        // Interposed exit here is used on purpose to inform bxl this process should be removed from its process table.
        exit(-1);
    }
}

// Executes objdump against the provided path to determine whether the binary is statically linked.
bool BxlObserver::is_statically_linked(const char *path)
{
    // Before running objdump, lets check if the path exists
    if (internal_access(path, F_OK) != 0) {
        return false;
    }

    char *args[] = {"", "-p", (char *)path, NULL};
    std::string result = execute_and_pipe_stdout(path, "/usr/bin/objdump", args);

    // Objdump should be able to dump the headers for any binary
    // If it doesn't show this output, then the file does not exist, or is not a binary
    std::string objDumpExeFound = "Program Header:";
    // This output confirms that the dynamic section in objdump contains libc
    std::string objDumpOutput = "NEEDED               libc.so.";

    return result.find(objDumpExeFound) != std::string::npos && result.find(objDumpOutput) == std::string::npos;
}

bool BxlObserver::contains_capabilities(const char *path)
{
    // Before running getcap, lets check if the path exists
    if (real_access(path, F_OK) != 0) {
        return false;
    }

    char *args[] = {"", (char *)path, NULL};
    std::string result = execute_and_pipe_stdout(path, "/usr/sbin/getcap", args);

    return !result.empty();
}

bool BxlObserver::ShouldBreakaway(int fd, char *const argv[])
{
    return ShouldBreakaway(fd_to_path(fd).c_str(), argv);
}

bool BxlObserver::ShouldBreakaway(const char *path, char *const argv[])
{
    auto commandLine = GetCommandLineFromArgv(argv);    

    return ShouldBreakaway(path, commandLine);
    
}

bool BxlObserver::ShouldBreakaway(const char *path, std::string &args, pid_t pid, pid_t ppid)
{
    bool result = fam_->ShouldBreakaway(path, args);
    
    if (result)
    {
        // Send a "process is about to break away" report so that the managed side can track it.
        auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   "breakaway",
            /* event_type */    buildxl::linux::EventType::kBreakAway,
            /* pid */           pid == -1 ? getpid() : pid,
            /* ppid */          ppid == -1 ? getppid() : ppid,
            /* error */         0,
            /* src_path */      path);
        event.SetSourceAccessCheck(AccessCheckResult(RequestedAccess::None, ResultAction::Allow, ReportLevel::Report));
        
        // It should be fine to use the primary pipe, a delay in reporting the breakaway can at most cost that we wait a little
        // longer to tear down the sandbox if the message arrives after the main process has exited (since we'll wait for this process
        // until it is identified as a breakaway). Today the secondary pipe is created only when ptrace is enabled, and unconditionally
        // creating it for this case sounds like a waste.
        SendReport(event, /* use_secondary_pipe */ false);
    }

    return result;
}

std::string BxlObserver::execute_and_pipe_stdout(const char *path, const char *process, char *const args[])
{
    std::string result;
    int pipefd[2];
    char mutablePath[PATH_MAX];

    pipe(pipefd);

    pid_t objDumpChild = real_fork();

    if (objDumpChild == 0)
    {
        // Child process to execute objdump
        real_close(pipefd[0]);    // close reading end in the child
        real_dup2(pipefd[1], 1);  // send stdout to the pipe
        real_dup2(pipefd[1], 2);  // send stderr to the pipe
        real_close(pipefd[1]);    // this descriptor is no longer needed

        char *envp[] = { NULL};

        real_execvpe(process, args, envp);

        real__exit(1); // If exec was successful then we should never reach this
    }
    else
    {
        char buffer[4096];
        real_close(pipefd[1]);  // close the write end of the pipe in the parent

        while (true)
        {
            auto bytesRead = read(pipefd[0], buffer, sizeof(buffer)-1);
            if (bytesRead == 0)
            {
                break;
            }

            buffer[bytesRead] = '\0';
            result.append(buffer);
        }

        // We need to waitpid on the child to allow the OS to release resources for a terminated child
        // We don't care about the result of the objdump process here, just need to waitpid to ensure proper cleanup
        int status;
        waitpid(objDumpChild, &status, 0);
    }

    return result;
}

void BxlObserver::disable_fd_table()
{
    useFdTable_ = false;
}

ssize_t BxlObserver::read_path_for_fd(int fd, char *buf, size_t bufsiz, pid_t associatedPid)
{
    char procPath[100] = {0};

    if (associatedPid == 0)
    {
        sprintf(procPath, "/proc/self/fd/%d", fd);
    }
    else
    {
        sprintf(procPath, "/proc/%d/fd/%d", associatedPid, fd);
    }
    
    return internal_readlink(procPath, buf, bufsiz);
}

void BxlObserver::reset_fd_table_entry(int fd)
{
    if (fd >= 0 && fd < MAX_FD)
    {
        fdTable_[fd] = empty_str_;
    }
}

void BxlObserver::reset_fd_table()
{
    for (int i = 0; i < MAX_FD; i++)
    {
        fdTable_[i] = empty_str_;
    }
}

std::string BxlObserver::fd_to_path(int fd, pid_t associatedPid)
{
    char path[PATH_MAX] = {0};

    if (fd < 0) {
        return "";
    }

    // ignore if fd is out of range
    if (fd >= MAX_FD)
    {
        read_path_for_fd(fd, path, PATH_MAX, associatedPid);
        return path;
    }

    if (useFdTable_)
    {
        // check the file descriptor table
        if (fdTable_[fd].length() > 0)
        {
            return fdTable_[fd];
        }
    }

    // read from the filesystem and update the file descriptor table
    ssize_t result = read_path_for_fd(fd, path, PATH_MAX, associatedPid);
    if (result != -1)
    {
        // Only cache if read_path_for_fd succeeded.
        if (useFdTable_)
        {
            fdTable_[fd] = path;
        }
    }

    return path;
}

void BxlObserver::report_intermediate_symlinks(const char *pathname, pid_t associatedPid, pid_t associatedParentPid)
{
    if (pathname == nullptr)
    {
        // Nothing to do
        return;
    }

    // Make it into an absolute path
    char fullPath[PATH_MAX] = {0};
    // associatedPid is irrelevant as we're using AT_FDCWD
    relative_to_absolute(pathname, AT_FDCWD, /* associatedPid */ 0, fullPath); 

    // This will report all intermediate symlinks in the path
    resolve_path(fullPath, /* followFinalSymlink */ true, associatedPid, associatedParentPid);
}

std::string BxlObserver::normalize_path_at(int dirfd, const char *pathname, pid_t associatedPid, pid_t associatedParentPid, int oflags, const char *systemcall)
{
    // Observe that dirfd is assumed to point to a directory file descriptor. Under that assumption, it is safe to call fd_to_path for it.
    // TODO: If we wanted to be very defensive, we could also consider the case of some tool invoking any of the *at(... dirfd ...) family with a 
    // descriptor that corresponds to a non-file. This would cause the call to fail, but it might poison the file descriptor table with a non-file
    // descriptor for which we could end up not invalidating it properly.

    // no pathname given --> read path for dirfd
    if (pathname == nullptr)
    {
        return fd_to_path(dirfd, associatedPid);
    }

    char fullPath[PATH_MAX] = {0};
    relative_to_absolute(pathname, dirfd, associatedPid, fullPath, systemcall);    

    bool followFinalSymlink = (oflags & O_NOFOLLOW) == 0;
    resolve_path(fullPath, followFinalSymlink, associatedPid, associatedParentPid);

    return fullPath;
}

void BxlObserver::relative_to_absolute(const char *pathname, int dirfd, int associatedPid, char *fullpath, const char *systemcall)
{
    size_t len = 0;

    // if relative path --> resolve it against dirfd
    if (*pathname != '/')
    {
        if (dirfd == AT_FDCWD)
        {
            if (!getcurrentworkingdirectory(fullpath, PATH_MAX, associatedPid))
            {
                _fatal("Could not get CWD; errno: %d, path: '%s'", errno, fullpath);
            }
            len = strlen(fullpath);
        }
        else
        {
            std::string dirPath = fd_to_path(dirfd, associatedPid);
            len = dirPath.length();
            strcpy(fullpath, dirPath.c_str());
        }

        if (len <= 0)
        {
            _fatal("['%s'] Could not get path for fd %d with path '%s'; errno: %d", systemcall, dirfd, pathname, errno);
        }

        if (pathname[0] != '\0')
        {
            fullpath[len] = '/';
            strcpy(fullpath + len + 1, pathname);
        }
    }
    else
    {
        strcpy(fullpath, pathname);
    }
}

static void shift_left(char *str, int n)
{
    do
    {
        *(str - n) = *str;
    } while (*str++);
}

static char* find_prev_slash(char *pStr)
{
    while (*--pStr != '/');
    return pStr;
}

// resolve any intermediate directory symlinks
void BxlObserver::resolve_path(char *fullpath, bool followFinalSymlink, pid_t associatedPid, pid_t associatedParentPid)
{
    if (fullpath == nullptr || fullpath[0] != '/')
    {
        LOG_DEBUG("Tried to resolve a string that is not an absolute path: %s", fullpath == nullptr ? "<NULL>" : fullpath);
        return;
    }

    if (associatedPid == 0)
    {
        associatedPid = getpid();
    }

    unordered_set<string> visited;

    char readlinkBuf[PATH_MAX];
    char *pFullpath = fullpath + 1;
    while (true)
    {
        // first handle "/../", "/./", and "//"
        if (*pFullpath == '/')
        {
            char *pPrevSlash = find_prev_slash(pFullpath);
            int parentDirLen = pFullpath - pPrevSlash - 1;
            if (parentDirLen == 0)
            {
                shift_left(pFullpath + 1, 1);
                continue;
            }
            else if (parentDirLen == 1 && *(pFullpath - 1) == '.')
            {
                shift_left(pFullpath + 1, 2);
                --pFullpath;
                continue;
            }
            else if (parentDirLen == 2 && *(pFullpath - 1) == '.' && *(pFullpath - 2) == '.')
            {
                // find previous slash unless already at the beginning
                if (pPrevSlash > fullpath)
                {
                    pPrevSlash = find_prev_slash(pPrevSlash);
                }
                int shiftLen = pFullpath - pPrevSlash;
                shift_left(pFullpath + 1, shiftLen);
                pFullpath = pPrevSlash + 1;
                continue;
            }
        }

        // call readlink for intermediate dirs and the final path if followSymlink is true
        ssize_t nReadlinkBuf = -1;
        char ch = *pFullpath;
        if (*pFullpath == '/' || (*pFullpath == '\0' && followFinalSymlink))
        {
            *pFullpath = '\0';
            nReadlinkBuf = internal_readlink(fullpath, readlinkBuf, PATH_MAX);
            *pFullpath = ch;
        }

        // if not a symlink --> either continue or exit if at the end of the path
        if (nReadlinkBuf == -1)
        {
            if (*pFullpath == '\0')
            {
                break;
            }
            else
            {
                ++pFullpath;
                continue;
            }
        }

        // current path is a symlink
        readlinkBuf[nReadlinkBuf] = '\0';

        // report readlink for the current path
        *pFullpath = '\0';
        // break if the same symlink has already been visited (breaks symlink loops)
        if (!visited.insert(fullpath).second) break;
        
        auto event = buildxl::linux::SandboxEvent::AbsolutePathSandboxEvent(
            /* system_call */   "_readlink",
            /* event_type */    buildxl::linux::EventType::kReadLink,
            /* pid */           associatedPid,
            /* ppid */          associatedParentPid,
            /* error */         0,
            /* src_path */      fullpath);

        // Don't normalize the paths here! We are exactly doing that right now...
        event.SetRequiredPathResolution(buildxl::linux::RequiredPathResolution::kDoNotResolve);    
        CreateAndReportAccess(event);
        
        *pFullpath = ch;

        // append the rest of the original path to the readlink target
        strcpy(
            readlinkBuf + nReadlinkBuf,
            (readlinkBuf[nReadlinkBuf-1] == '/' && *pFullpath == '/') ? pFullpath + 1 : pFullpath);

        // if readlink target is an absolute path -> overwrite fullpath with it and start from the beginning
        if (readlinkBuf[0] == '/')
        {
            strcpy(fullpath, readlinkBuf);
            pFullpath = fullpath + 1;
            continue;
        }

        // readlink target is a relative path -> replace the current dir in fullpath with the target
        pFullpath = find_prev_slash(pFullpath);
        strcpy(++pFullpath, readlinkBuf);
    }
}

char** BxlObserver::ensure_env_value_with_log(char *const envp[], char const *envName, char const *envValue)
{
    char **newEnvp = ensure_env_value(envp, envName, envValue);
    if (newEnvp != envp)
    {
        LOG_DEBUG("envp has been modified with %s added to %s", envValue, envName);
    }

    return newEnvp;
}

char** BxlObserver::removeEnvs(char *const envp[])
{
    char **newEnvp = remove_path_from_LDPRELOAD(envp, detoursLibFullPath_);
    newEnvp = ensure_env_value(newEnvp, BxlEnvFamPath, "");
    newEnvp = ensure_env_value(newEnvp, BxlEnvDetoursPath, "");
    newEnvp = ensure_env_value(newEnvp, BxlEnvRootPid, "");
    newEnvp = ensure_env_value(newEnvp, BxlPTraceForcedProcessNames, "");
    return newEnvp;
}

// Propagate the environment needed for sandbox initialization
char** BxlObserver::ensureEnvs(char *const envp[])
{
    if (!IsMonitoringChildProcesses())
    {
        return removeEnvs(envp);
    }
    else
    {
        char **newEnvp = ensure_paths_included_in_env(envp, LD_PRELOAD_ENV_VAR_PREFIX, detoursLibFullPath_, NULL);
        if (newEnvp != envp)
        {
            LOG_DEBUG("envp has been modified with %s added to %s", detoursLibFullPath_, "LD_PRELOAD");
        }

        // Keep in sync with removeEnvs above.
        newEnvp = ensure_env_value_with_log(newEnvp, BxlEnvFamPath, famPath_);
        newEnvp = ensure_env_value_with_log(newEnvp, BxlEnvDetoursPath, detoursLibFullPath_);
        newEnvp = ensure_env_value(newEnvp, BxlEnvRootPid, "");
        newEnvp = ensure_env_value_with_log(newEnvp, BxlPTraceForcedProcessNames, forcedPTraceProcessNamesList_);

        return newEnvp;
    }
}

bool BxlObserver::EnumerateDirectory(std::string rootDirectory, bool recursive, std::vector<std::string>& filesAndDirectories)
{
    std::stack<std::string> directoriesToEnumerate;
    DIR *dir;
    struct dirent *ent;

    filesAndDirectories.clear();
    directoriesToEnumerate.push(rootDirectory);
    filesAndDirectories.push_back(rootDirectory);

    while (!directoriesToEnumerate.empty())
    {
        auto currentDirectory = directoriesToEnumerate.top();
        directoriesToEnumerate.pop();

        dir = internal_opendir(currentDirectory.c_str());

        if (dir != NULL)
        {
            while ((ent = internal_readdir(dir)) != NULL)
            {
                std::string fileOrDirectory(ent->d_name);
                if (fileOrDirectory == "." || fileOrDirectory == "..")
                {
                    continue;
                }

                std::string fullPath = currentDirectory + "/" + fileOrDirectory;

                // NOTE: d_type is supported on these filesystems as of 2022 which should cover all BuildXL cases: Btrfs, ext2, ext3, and ext4
                if (ent->d_type == DT_DIR && recursive)
                {
                    // DT_DIR = Directory
                    directoriesToEnumerate.push(fullPath);
                }

                filesAndDirectories.push_back(fullPath);
            }

            internal_closedir(dir);
        }
        else
        {
            // Something went wrong with opendir
            // TODO [pgunasekara]: change this to _fatal
            LOG_DEBUG("[BxlObserver::EnumerateDirectory] opendir failed on '%s' with errno %d\n", currentDirectory, errno);
            return false;
        }
    }

    return true;
}
