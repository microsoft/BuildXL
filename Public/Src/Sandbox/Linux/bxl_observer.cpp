// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include <algorithm>
#include "bxl_observer.hpp"
#include "IOHandler.hpp"
#include "observer_utilities.hpp"
#include <stack>
#include <sys/prctl.h>
#include <sys/wait.h>

static void HandleAccessReport(AccessReport report, int _)
{
    BxlObserver::GetInstance()->SendReport(report);
}

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
        real_readlink("/proc/self/exe", progFullPath_, PATH_MAX);
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
    if (CheckEnableLinuxPTraceSandbox(pip_->GetFamExtraFlags()))
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
        real_sem_close(messageCountingSemaphore_);
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
    FILE *famFile = real_fopen(famPath_, "rb");
    if (!famFile)
    {
        _fatal("Could not open file '%s'; errno: %d", famPath_, errno);
    }

    fseek(famFile, 0, SEEK_END);
    long famLength = ftell(famFile);
    rewind(famFile);

    auto famPayload = new char [famLength];
    real_fread(famPayload, famLength, 1, famFile);
    real_fclose(famFile);

    // create SandboxedPip (which parses FAM and throws on error)
    pip_ = shared_ptr<SandboxedPip>(new SandboxedPip(pid, famPayload, famLength));

    // create sandbox
    sandbox_ = new Sandbox(0, Configuration::DetoursLinuxSandboxType);

    // initialize sandbox
    if (!sandbox_->TrackRootProcess(pip_))
    {
        _fatal("Could not track root process %s:%d", __progname, pid);
    }

    process_ = sandbox_->FindTrackedProcess(pid);
    process_->SetPath(progFullPath_);
    sandbox_->SetAccessReportCallback(HandleAccessReport);

    sandboxLoggingEnabled_ = CheckEnableLinuxSandboxLogging(pip_->GetFamExtraFlags());
}

void BxlObserver::Init()
{
    // If message counting is enabled, open the associated semaphore (this should already be created by the managed side)
    if (CheckCheckDetoursMessageCount(pip_->GetFamFlags()))
    {
        // Setting initializingSemaphore_ will communicate to the interpose layer to not interpose any libc functions called inside sem_open
        initializingSemaphore_ = true;
        messageCountingSemaphore_ = real_sem_open(pip_->GetInternalDetoursErrorNotificationFile(), O_CREAT, 0644, 0);

        if (messageCountingSemaphore_ == SEM_FAILED)
        {
            // we'll log a message here, but this won't fail the pip until this feature is tested more thorougly
            real_fprintf(stdout, "BuildXL injected message: File access monitoring failed to open message counting semaphore '%s' with errno: '%d'. You should rerun this build, or contact the BuildXL team if the issue persists across multiple builds.", pip_->GetInternalDetoursErrorNotificationFile(), errno);
        }

        initializingSemaphore_ = false;
    }

    bxlObserverInitialized_= true;
}

// Access Reporting
AccessCheckResult BxlObserver::CreateAccess(const char *syscall_name, buildxl::linux::SandboxEvent& event, AccessReportGroup& report_group, bool check_cache) {
    if (!event.IsValid()) {
        LOG_DEBUG("Won't report an access for syscall %s because the event is invalid.", syscall_name); 
        return sNotChecked;
    }

    // Get mode if not already set by caller
    // Resolve paths and mode
    bool isFileEvent = ResolveEventPaths(event);

    // Check if non-file, we don't want to report these.
    if (!isFileEvent) {
        return sNotChecked;
    }
    
    // Check cache and return early if this access has already been checked.
    if (check_cache && IsCacheHit(event.GetEventType(), event.GetSrcPath(), event.GetDstPath())) {
        return sNotChecked;
    }

    // Perform access check
    auto result = sNotChecked;
    auto access_should_be_blocked = false;

    if (IsEnabled(event.GetPid())) {
        IOHandler handler(sandbox_);
        handler.SetProcess(process_);

        // We convert this to an IOEvent here because the macos handler expects an IOEvent. This can be removed once that dependency is removed
        // TODO [pgunasekara]: Remove this once we remove IOEvent
        IOEvent io_event(
            /* pid */       event.GetPid(),
            /* cpid */      event.GetEventType() == ES_EVENT_TYPE_NOTIFY_FORK ? event.GetChildPid() : 0,
            /* ppid */      0,
            /* type */      event.GetEventType(),
            /* action */    ES_ACTION_TYPE_NOTIFY,
            /* src */       event.GetSrcPath(),
            /* dst */       event.GetDstPath(),
            /* exec */      event.GetEventType() == ES_EVENT_TYPE_NOTIFY_FORK ? event.GetSrcPath() : progFullPath_,
            /* mode */      event.GetMode(),
            /* modified */  false,
            /* error */     event.GetError());

        result = handler.CheckAccessAndBuildReport(io_event, report_group);
        access_should_be_blocked = result.ShouldDenyAccess() && IsFailingUnexpectedAccesses();
        report_group.SetErrno(event.GetError());

        if (!access_should_be_blocked) {
            // This access won't be blocked, so let's cache it.
            // We cache event types that are always a miss in IsCacheHit, but this also should be fine.
            CheckCache(event.GetEventType(), event.GetSrcPath(), /* addEntryIfMissing */ true);
        }
    }

    // Log path
    LOG_DEBUG("(( %10s:%2d )) %s %s%s", syscall_name, event.GetEventType(), event.GetSrcPath().c_str(),
        !result.ShouldReport() ? "[Ignored]" : result.ShouldDenyAccess() ? "[Denied]" : "[Allowed]",
        access_should_be_blocked ? "[Blocked]" : "");

    return result;
}

void BxlObserver::ReportAccess(const AccessReportGroup& report_group) {
    SendReport(report_group);
}

void BxlObserver::CreateAndReportAccess(const char *syscall_name, buildxl::linux::SandboxEvent& event, bool check_cache) {
    auto report_group = AccessReportGroup();
    CreateAccess(syscall_name, event, report_group, check_cache);
    ReportAccess(report_group);
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
    switch (event.GetPathType()) {
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
            NormalizeEventPaths(event, resolved_path_src, resolved_path_dst);
            event.UpdatePaths(resolved_path_src, resolved_path_dst);        
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

            NormalizeEventPaths(event, resolved_path_src, resolved_path_dst);

            event.UpdatePaths(resolved_path_src, resolved_path_dst);

            // Update the mode after normalization, so we use an absolute path for it
            if (event.GetMode() == 0) {
                event.SetMode(get_mode(event.GetSrcPath().c_str()));
            }
            break;
        } 
        case buildxl::linux::SandboxEventPathType::kAbsolutePaths: {
            // Paths already resolved but may need to be normalized
            if (event.PathNeedsNormalization()) {
                char resolved_path_src[PATH_MAX] = { 0 };
                char resolved_path_dst[PATH_MAX] = { 0 };

                strncpy(resolved_path_src, event.GetSrcPath().c_str(), PATH_MAX);
                strncpy(resolved_path_dst, event.GetDstPath().c_str(), PATH_MAX);

                NormalizeEventPaths(event, resolved_path_src, resolved_path_dst);
                event.UpdatePaths(resolved_path_src, resolved_path_dst);
            }

            // Update the mode after normalization
            if (event.GetMode() == 0) {
                event.SetMode(get_mode(event.GetSrcPath().c_str()));
            }
            break;
        }
        default:
            break;
    }

    return true;
}

void BxlObserver::NormalizeEventPaths(buildxl::linux::SandboxEvent& event, char *src_path, char *dst_path) {
    // Normalize paths if a normalization flag was set
    if (event.PathNeedsNormalization()) {
        bool follow_symlink = (event.GetNormalizationFlags() & O_NOFOLLOW) == 0;
        resolve_path(src_path, follow_symlink, event.GetPid());

        if (!event.GetDstPath().empty()) {
            resolve_path(dst_path, follow_symlink, event.GetPid());
        }
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

void BxlObserver::LogDebug(pid_t pid, const char *fmt, ...)
{
    if (LogDebugEnabled())
    {
        // Build an access report that represents the debug message
        AccessReport debugReport = 
        {
            .operation          = kOpDebugMessage,
            .pid                = pid,
            .rootPid            = pip_->GetProcessId(),
            .requestedAccess    = (int)RequestedAccess::Read,
            .status             = FileAccessStatus::FileAccessStatus_Allowed,
            .reportExplicitly   = 0,
            .error              = 0,
            .pipId              = pip_->GetPipId(),
            .path               = {0},
            .stats              = {0},
            .isDirectory        = 0,
            .shouldReport       = true,
        };

        va_list args;
        va_start(args, fmt);
        // We (re)use the path for the debug message in order to not change the report format just for debugging
        // So we limit the message to MAXPATHLEN (~4k chars, which should be enough)
        int numWritten = vsnprintf(debugReport.path, MAXPATHLEN, fmt, args);
        va_end(args);

        // Sanitize the debug message so we don't confuse the parser on managed code:
        // Pipes (|) are used to delimit the message parts and we expect one line (\n) per report, so
        // replace those occurrences with something else.
        for (int i = 0 ; i < MAXPATHLEN; i++)
        {
            if (debugReport.path[i] == '|')
            {
                debugReport.path[i] = '!';
            }
            
            if (debugReport.path[i] == '\n' || debugReport.path[i] == '\r')
            {
                debugReport.path[i] = '.';
            }
        }

        SendReport(debugReport, /* isDebugMessage */ true);
    }
}

// Checks whether cache contains (event, path) pair and returns the result of this check.
// If the pair is not in cache and addEntryIfMissing is true, attempts to add the pair to cache.
bool BxlObserver::CheckCache(es_event_type_t event, const string &path, bool addEntryIfMissing)
{
    // coalesce some similar events
    es_event_type_t key;
    switch (event)
    {
        case ES_EVENT_TYPE_NOTIFY_TRUNCATE:
        case ES_EVENT_TYPE_NOTIFY_SETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_SETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_DELETEEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_SETFLAGS:
        case ES_EVENT_TYPE_NOTIFY_SETOWNER:
        case ES_EVENT_TYPE_NOTIFY_SETMODE:
        case ES_EVENT_TYPE_NOTIFY_WRITE:
        case ES_EVENT_TYPE_NOTIFY_UTIMES:
        case ES_EVENT_TYPE_NOTIFY_SETTIME:
        case ES_EVENT_TYPE_NOTIFY_SETACL:
            key = ES_EVENT_TYPE_NOTIFY_WRITE;
            break;

        case ES_EVENT_TYPE_NOTIFY_GETATTRLIST:
        case ES_EVENT_TYPE_NOTIFY_GETEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_LISTEXTATTR:
        case ES_EVENT_TYPE_NOTIFY_ACCESS:
        case ES_EVENT_TYPE_NOTIFY_STAT:
            key = ES_EVENT_TYPE_NOTIFY_STAT;

        default:
            key = event;
            break;
    }

    // This code could possibly be executing from an interrupt routine or from who knows where,
    // so to avoid deadlocks it's essential to never block here indefinitely.
    if (!cacheMtx_.try_lock_for(chrono::milliseconds(1)))
    {
        return false; // failed to acquire mutex -> forget about it
    }

    // ============================== in the critical section ================================

    // make sure the mutex is released by the end
    shared_ptr<timed_mutex> sp(&cacheMtx_, [](timed_mutex *mtx) { mtx->unlock(); });

    unordered_map<es_event_type_t, unordered_set<string>>::iterator it = cache_.find(key);
    if (it == cache_.end())
    {
        if (addEntryIfMissing) 
        {
            unordered_set<string> set;
            set.insert(path);
            cache_.insert(make_pair(key, set));
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

bool BxlObserver::IsCacheHit(es_event_type_t event, const string &path, const string &secondPath)
{
    // (1) IMPORTANT           : never do any of this stuff after this object has been disposed!
    //     WHY                 : because the cache date structure is invalid at that point.
    //     HOW CAN THIS HAPPEN : we may get called from "on_exit" handlers, at which point the
    //                           global BxlObserver singleton instance can already be disposed.
    // (2) never cache FORK, EXEC, EXIT and events that take 2 paths
    if (disposed_ ||
        secondPath.length() > 0 ||
        event == ES_EVENT_TYPE_NOTIFY_FORK ||
        event == ES_EVENT_TYPE_NOTIFY_EXEC ||
        event == ES_EVENT_TYPE_NOTIFY_EXIT)
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

bool BxlObserver::SendExitReport(pid_t pid)
{
    IOHandler handler(sandbox_);
    handler.SetProcess(process_);
    AccessReport report;
    handler.CreateReportProcessExited(pid == 0 ? getpid() : pid, report);
    return SendReport(report);
}

bool BxlObserver::SendReport(const AccessReportGroup &report)
{
    bool result = report.firstReport.shouldReport 
        ? SendReport(report.firstReport)
        : true;

    result &= report.secondReport.shouldReport
        ? SendReport(report.secondReport)
        : true;

    return result;
}

bool BxlObserver::SendReport(const AccessReport &report, bool isDebugMessage, bool useSecondaryPipe)
{
    // there is no central sendbox process here (i.e., there is an instance of this
    // guy in every child process), so counting process tree size is not feasible
    if (report.operation == FileOperation::kOpProcessTreeCompleted)
    {
        return true;
    }

    // The BxlObserver isn't ready to send reports yet (usually because the message counting semaphore isn't yet initialized)
    bool unexpectedReport = !bxlObserverInitialized_ && report.operation != FileOperation::kOpDebugMessage;
    const int PrefixLength = sizeof(uint);
    char buffer[PIPE_BUF] = {0};
    int maxMessageLength = PIPE_BUF - PrefixLength;
    int reportSize = BuildReport(&buffer[PrefixLength], maxMessageLength, report, report.path, unexpectedReport);
    // CODESYNC: Public/Src/Engine/Processes/SandboxedProcessUnix.cs
    bool shouldCountReportType = 
        report.operation != FileOperation::kOpProcessStart
        && report.operation != FileOperation::kOpProcessExit
        && report.operation != FileOperation::kOpProcessTreeCompleted
        && report.operation != FileOperation::kOpDebugMessage;

    if (reportSize >= maxMessageLength)
    {
        // For debug messages it is fine to truncate the message, otherwise, this is a problem and we must fail
        if (!isDebugMessage)
        {
            // TODO: once 'send' is capable of sending more than PIPE_BUF at once, allocate a bigger buffer and send that
            _fatal("Message truncated to fit PIPE_BUF (%d): %s", PIPE_BUF, buffer);
        }
        else
        {
            // The report couldn't be fully built for a debug message. Let's crop the message (report.path) so it fits.
            // We calculate the maximum size allowed, considering that 'path' is the last component of the
            // message (plus the \n that ends any report, hence the -1), so it's the last thing 
            // we tried to write when hitting the size limit.
            int truncatedSize = strlen(report.path) - (reportSize - maxMessageLength) - 1;
            char truncatedMessage[truncatedSize] = {0};

            // Let's leave an ending \0
            strncpy(truncatedMessage, report.path, truncatedSize - 1);

            reportSize = BuildReport(&buffer[PrefixLength], maxMessageLength, report, truncatedMessage, unexpectedReport);
        }
    }

    *(uint*)(buffer) = reportSize;
    return Send(buffer, std::min(reportSize + PrefixLength, PIPE_BUF), useSecondaryPipe, shouldCountReportType);
}

void BxlObserver::report_exec(const char *syscallName, const char *procName, const char *file, int error, mode_t mode, pid_t associatedPid)
{
    if (IsMonitoringChildProcesses())
    {
        // first report 'procName' as is (without trying to resolve it) to ensure that a process name is reported before anything else
        report_access(syscallName, ES_EVENT_TYPE_NOTIFY_EXEC, procName, empty_str_, mode, error, /* checkCache */ true, associatedPid);
        report_access(syscallName, ES_EVENT_TYPE_NOTIFY_EXEC, file, mode, /*flags*/ 0, error, /* checkCache */ true, associatedPid);
    }
}

void BxlObserver::report_exec_args(pid_t pid)
{
    if (IsReportingProcessArgs())
    {
        char path[PATH_MAX] = { 0 };
        int maxSize = PIPE_BUF + sizeof(uint) - 1;
        char cmdLineBuffer[maxSize] = { 0 };
        std::string cmdLine;
        bool firstArg = true;

        // /proc/<pid>/cmdline has a set of arguments separated by the null terminator
        snprintf(path, PATH_MAX, "/proc/%d/cmdline", pid);

        int fd = open(path, O_RDONLY);
        int bytesRead = read(fd, cmdLineBuffer, maxSize);
        char *end = cmdLineBuffer + bytesRead;

        for (char *currentArg = cmdLineBuffer; currentArg < end; )
        {
            if (firstArg)
            {
                firstArg = false;
            }
            else
            {
                cmdLine.append(" ");
            }
            cmdLine.append(currentArg);

            // Increment currentArg until the next null character is reached
            while(*currentArg++);
        }
        close(fd);

        report_exec_args(pid, cmdLine.c_str());
    }
}

void BxlObserver::report_exec_args(pid_t pid, const char *args) {
    if (IsReportingProcessArgs())
    {
        AccessReport report =
        {
            .operation        = kOpProcessCommandLine,
            .pid              = pid,
            .rootPid          = pip_->GetProcessId(),
            .requestedAccess  = (int) RequestedAccess::Read,
            .status           = FileAccessStatus::FileAccessStatus_Allowed,
            .reportExplicitly = (int) ReportLevel::Report,
            .error            = 0,
            .pipId            = pip_->GetPipId(),
            .path             = {0},
            .stats            = {0},
            .isDirectory      = 0,
            .shouldReport     = true,
        };

        // This may truncate the path but that's okay because it's the process command line.
        strlcpy(report.path, args, sizeof(report.path));
        SendReport(report, /* isDebugMessage */ false, /* useSecondaryPipe */ false);
    }
}

void BxlObserver::report_access(const char *syscallName, es_event_type_t eventType, const char *reportPath, const char *secondPath, mode_t mode, int error, bool checkCache, pid_t associatedPid)
{
    if (reportPath == nullptr || secondPath == nullptr) 
    {
        // If we're given null paths, we can't really build a report. Just return.
        LOG_DEBUG("Can't report an access for syscall %s with a null path. reportPath = %p, secondPath %p", syscallName, reportPath, secondPath); 
        return;
    }
    
    report_access_internal(syscallName, eventType, reportPath, secondPath, mode, error, checkCache, associatedPid);
}

void BxlObserver::report_access_internal(const char *syscallName, es_event_type_t eventType, const char *reportPath, const char *secondPath, mode_t mode, int error, bool checkCache, pid_t associatedPid)
{
    AccessReportGroup report;
    create_access_internal(syscallName, eventType, reportPath, secondPath, report, mode, checkCache, associatedPid);
    report.SetErrno(error);
    SendReport(report);
}

AccessCheckResult BxlObserver::create_access(const char *syscallName, es_event_type_t eventType, const char *reportPath, const char *secondPath, AccessReportGroup &reportGroup, mode_t mode, bool checkCache, pid_t associatedPid)
{
    if (reportPath == nullptr || secondPath == nullptr)
    {
        LOG_DEBUG("Can't create an access for syscall %s with a null path. reportPath = %p, secondPath %p", syscallName, reportPath, secondPath); 
        return sNotChecked;
    }

    return create_access_internal(syscallName, eventType, reportPath, secondPath, reportGroup, mode, checkCache, associatedPid);
}

AccessCheckResult BxlObserver::create_access_internal(const char *syscallName, es_event_type_t eventType, const char *reportPath, const char *secondPath, AccessReportGroup &reportGroup, mode_t mode, bool checkCache, pid_t associatedPid)
{
    secondPath = secondPath == nullptr ? empty_str_ : secondPath;  
    if (checkCache && IsCacheHit(eventType, std::move(std::string(reportPath)), std::move(std::string(secondPath))))
    {
        return sNotChecked;
    }

    if (mode == 0)
    {
        // Mode hasn't been computed yet. Let's do it here.
        mode = get_mode(reportPath);
    }

    // If this file descriptor is a non-file (e.g., a pipe, or socket, etc.) then we don't care about it
    if (is_non_file(mode))
    {
        return sNotChecked;
    }

    std::string execPath = eventType == ES_EVENT_TYPE_NOTIFY_EXEC
        ? std::string(reportPath)
        : std::string(progFullPath_);

    IOEvent event(associatedPid == 0 ? getpid() : associatedPid, 0, getppid(), eventType, ES_ACTION_TYPE_NOTIFY, std::move(std::string(reportPath)), std::move(std::string(secondPath)), std::move(execPath), mode, false);
    return create_access(syscallName, event, reportGroup, /* checkCache */ false /* because already checked cache above */);
}

void BxlObserver::report_access(const char *syscallName, IOEvent &event, bool checkCache)
{
    AccessReportGroup report;
    create_access(syscallName, event, report, checkCache);
    SendReport(report);
}

AccessCheckResult BxlObserver::create_access(const char *syscallName, IOEvent &event, AccessReportGroup &reportGroup, bool checkCache)
{
    es_event_type_t eventType = event.GetEventType();
    
    if (checkCache && IsCacheHit(eventType, event.GetSrcPath(), event.GetDstPath()))
    {
        return sNotChecked;
    }

    AccessCheckResult result = sNotChecked;
    pid_t pid = event.GetPid() == 0 ? getpid() : event.GetPid();
    bool accessShouldBeBlocked = false;

    if (IsEnabled(pid))
    {
        IOHandler handler(sandbox_);
        handler.SetProcess(process_);
        result = handler.CheckAccessAndBuildReport(event, reportGroup);
        accessShouldBeBlocked = result.ShouldDenyAccess() && IsFailingUnexpectedAccesses();
        if (!accessShouldBeBlocked) 
        {
            // This access won't be blocked, so let's cache it.
            // We populate cache even if checkCache is false, but this should be ok.
            // We cache event types that are always a miss in IsCacheHit, but this also shoulld be fine.
            CheckCache(eventType, event.GetSrcPath(), /* addEntryIfMissing */ true);
        }
    }

    LOG_DEBUG("(( %10s:%2d )) %s %s%s", syscallName, event.GetEventType(), event.GetEventPath(),
        !result.ShouldReport() ? "[Ignored]" : result.ShouldDenyAccess() ? "[Denied]" : "[Allowed]",
        accessShouldBeBlocked ? "[Blocked]" : "");

    return result;
}

void BxlObserver::report_access(const char *syscallName, es_event_type_t eventType, const char *pathname, mode_t mode, int flags, int error, bool checkCache, pid_t associatedPid)
{
    // If the path is null or if we can't normalize it, we have no meaningful way of reporting this access
    if (pathname == nullptr)
    {
        LOG_DEBUG("Can't report an access for syscall %s with a null path.", syscallName); 
        return;
    }

    auto normalized = normalize_path(pathname, flags, associatedPid);
    if (normalized.length() == 0) 
    {
        LOG_DEBUG("Couldn't normalize path %s", pathname);
        return;
    }

    report_access_internal(syscallName, eventType, normalized.c_str(), /*secondPath*/ nullptr , mode, error, checkCache, associatedPid);
}

AccessCheckResult BxlObserver::create_access(const char *syscallName, es_event_type_t eventType, const char *pathname, AccessReportGroup &reportGroup, mode_t mode, int flags, bool checkCache, pid_t associatedPid)
{
    // If the path is null or if we can't normalize it, we have no meaningful way of reporting this access
    if (pathname == nullptr) 
    {
        return sNotChecked;
    }

    auto normalized = normalize_path(pathname, flags, associatedPid);
    if (normalized.length() == 0)
    {
        return sNotChecked;
    }

    return create_access_internal(syscallName, eventType, normalized.c_str(), /* secondPath */ nullptr, reportGroup, mode, checkCache, associatedPid);
}

void BxlObserver::report_access_fd(const char *syscallName, es_event_type_t eventType, int fd, int error, pid_t associatedPid)
{   
    AccessReportGroup report;
    create_access_fd(syscallName, eventType, fd, report, associatedPid);
    report.SetErrno(error);
    SendReport(report);
}

AccessCheckResult BxlObserver::create_access_fd(const char *syscallName, es_event_type_t eventType, int fd, AccessReportGroup &report, pid_t associatedPid)
{   
    mode_t mode = get_mode(fd);

    // If this file descriptor is a non-file (e.g., a pipe, or socket, etc.) then we don't care about it
    if (is_non_file(mode))
    {
        return sNotChecked; 
    }

    std::string fullpath = fd_to_path(fd, associatedPid);

    // Only reports when fd_to_path succeeded.
    return fullpath.length() > 0
        ? create_access_internal(syscallName, eventType, fullpath.c_str(), /* secondPath */ nullptr, report, mode, /* checkCache */ true, associatedPid)
        : sNotChecked;
}

bool BxlObserver::is_non_file(const mode_t mode)
{
    // Observe we don't care about block devices here. It is unlikely that we'll support them e2e, this is just an FYI.
    return mode != 0 && !S_ISDIR(mode) && !S_ISREG(mode) && !S_ISLNK(mode);
}

AccessCheckResult BxlObserver::create_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname, AccessReportGroup &report, int flags, bool getModeWithFd, pid_t associatedPid)
{
    if (pathname == nullptr)
    {
        LOG_DEBUG("Can't create an access for syscall %s with a null path.", syscallName); 
        return sNotChecked;
    }

    if (pathname[0] == '/')
    {
        return create_access(syscallName, eventType, pathname, report, /* mode */0, flags, /* checkCache */ true, associatedPid);
    }

    char fullpath[PATH_MAX] = {0};
    ssize_t len = 0;

    mode_t mode = 0;

    if (dirfd == AT_FDCWD)
    {
        if (!getcurrentworkingdirectory(fullpath, PATH_MAX, associatedPid))
        {
            return sNotChecked;
        }
        len = strlen(fullpath);
    }
    else
    {
        std::string dirPath;

        // If getModeWithFd is set, then we can call get_mode directly with the file descriptor instead of a path
        // If false, then use the provided associatedPid to convert the fd to a path and the get_mode on the path
        if (getModeWithFd)
        {
            mode = get_mode(dirfd);
        }
        else
        {
            dirPath = fd_to_path(dirfd, associatedPid);
            mode = get_mode(dirPath.c_str());
        }

        // If this file descriptor is a non-file (e.g., a pipe, or socket, etc.) then we don't care about it
        if (is_non_file(mode)) 
        {
            return sNotChecked;
        }

        if (dirPath.empty())
        {
            dirPath = fd_to_path(dirfd);
        }
        
        len = dirPath.length();
        strcpy(fullpath, dirPath.c_str());
    }

    if (len <= 0)
    {
        _fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
    }

    snprintf(&fullpath[len], PATH_MAX - len, "/%s", pathname);
    return create_access(syscallName, eventType, fullpath, report, mode, flags, /* checkCache */ true, associatedPid);
}

void BxlObserver::report_access_at(const char *syscallName, es_event_type_t eventType, int dirfd, const char *pathname, int flags, bool getModeWithFd, pid_t associatedPid, int error)
{
    AccessReportGroup report;
    create_access_at(syscallName, eventType, dirfd, pathname, report, flags, getModeWithFd, associatedPid);
    report.SetErrno(error);
    SendReport(report);
}

void BxlObserver::report_firstAllowWriteCheck(const char *fullPath)
{
    mode_t mode = get_mode(fullPath);
    bool fileExists = mode != 0 && !S_ISDIR(mode);
     
    AccessReport report =
        {
            .operation        = kOpFirstAllowWriteCheckInProcess,
            .pid              = getpid(),
            .rootPid          = pip_->GetProcessId(),
            .requestedAccess  = (int) RequestedAccess::Write,
            .status           = fileExists ? FileAccessStatus::FileAccessStatus_Denied : FileAccessStatus::FileAccessStatus_Allowed,
            .reportExplicitly = (int) ReportLevel::Report,
            .error            = 0,
            .pipId            = pip_->GetPipId(),
            .path             = {0},
            .stats            = {0},
            .isDirectory      = (uint)S_ISDIR(mode),
            .shouldReport     = true
        };

    strlcpy(report.path, fullPath, sizeof(report.path));

    SendReport(report);

    AccessCheckResult result(RequestedAccess::Write, fileExists ? ResultAction::Deny : ResultAction::Allow, ReportLevel::Report);
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
    if (!CheckEnableLinuxPTraceSandbox(pip_->GetFamExtraFlags()))
    {
        return false;
    }

    if (IsPTraceForced(path) || CheckUnconditionallyEnableLinuxPTraceSandbox(pip_->GetFamExtraFlags()))
    {
        // Allow this process to be traced by the tracer process.
        set_ptrace_permissions();

        // We force ptrace for this process. 
        // Send a "process requires ptrace" report so that the managed side can track it.
        AccessReport report =
        {
            .operation        = kOpProcessRequiresPtrace,
            .pid              = getpid(),
            .rootPid          = pip_->GetProcessId(),
            .requestedAccess  = (int) RequestedAccess::Read,
            .status           = FileAccessStatus::FileAccessStatus_Allowed,
            .reportExplicitly = (int) ReportLevel::Report,
            .error            = 0,
            .pipId            = pip_->GetPipId(),
            .path             = {0},
            .stats            = {0},
            .isDirectory      = 0,
            .shouldReport     = true,
        };

        strlcpy(report.path, path, sizeof(report.path));
        SendReport(report, /* isDebugMessage */ false, /* useSecondaryPipe */ true);
        return true;
    }

    // Stat the path to get the last modified time of the path
    // We need to do this because the executable file could be changed in between this stat and the previous stat
    // If it was changed (has a different modified time), then we should run the check on it once more
    struct stat statbuf;
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
        real___lxstat(1, path, &statbuf);
#else
        real_lstat(path, &statbuf);
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

        AccessReport report =
        {
            .operation        = kOpProcessRequiresPtrace,
            .pid              = getpid(),
            .rootPid          = pip_->GetProcessId(),
            .requestedAccess  = (int) RequestedAccess::Read,
            .status           = FileAccessStatus::FileAccessStatus_Allowed,
            .reportExplicitly = (int) ReportLevel::Report,
            .error            = 0,
            .pipId            = pip_->GetPipId(),
            .path             = {0},
            .stats            = {0},
            .isDirectory      = 0,
            .shouldReport     = true,
        };

        strlcpy(report.path, path, sizeof(report.path));

        SendReport(report, /* isDebugMessage */ false, /* useSecondaryPipe */ true);
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
    if (real_access(path, F_OK) != 0) {
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
    
    return real_readlink(procPath, buf, bufsiz);
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

void BxlObserver::report_intermediate_symlinks(const char *pathname, pid_t associatedPid)
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
    resolve_path(fullPath, /* followFinalSymlink */ true, associatedPid);
}

std::string BxlObserver::normalize_path_at(int dirfd, const char *pathname, int oflags, pid_t associatedPid)
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
    relative_to_absolute(pathname, dirfd, associatedPid, fullPath);    

    bool followFinalSymlink = (oflags & O_NOFOLLOW) == 0;
    resolve_path(fullPath, followFinalSymlink, associatedPid);

    return fullPath;
}

void BxlObserver::relative_to_absolute(const char *pathname, int dirfd, int associatedPid, char *fullpath)
{
    size_t len = 0;

    // if relative path --> resolve it against dirfd
    if (*pathname != '/')
    {
        if (dirfd == AT_FDCWD)
        {
            if (!getcurrentworkingdirectory(fullpath, PATH_MAX, associatedPid))
            {
                _fatal("Could not get CWD; errno: %d", errno);
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
            _fatal("Could not get path for fd %d; errno: %d", dirfd, errno);
        }

        fullpath[len] = '/';
        strcpy(fullpath + len + 1, pathname);
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
void BxlObserver::resolve_path(char *fullpath, bool followFinalSymlink, pid_t associatedPid)
{
    if (fullpath == nullptr || fullpath[0] != '/')
    {
        LOG_DEBUG("Tried to resolve a string that is not an absolute path: %s", fullpath == nullptr ? "<NULL>" : fullpath);
        return;
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
            nReadlinkBuf = real_readlink(fullpath, readlinkBuf, PATH_MAX);
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
        report_access_internal("_readlink", ES_EVENT_TYPE_NOTIFY_READLINK, fullpath, /* secondPath */ (const char *)nullptr, /* mode */ 0, /* error */ 0, /* checkCache */ true, associatedPid);
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

// Propagate the environment needed for sandbox initialization
char** BxlObserver::ensureEnvs(char *const envp[])
{
    if (!IsMonitoringChildProcesses())
    {
        char **newEnvp = remove_path_from_LDPRELOAD(envp, detoursLibFullPath_);
        newEnvp = ensure_env_value(newEnvp, BxlEnvFamPath, "");
        newEnvp = ensure_env_value(newEnvp, BxlEnvDetoursPath, "");
        newEnvp = ensure_env_value(newEnvp, BxlEnvRootPid, "");
        newEnvp = ensure_env_value(newEnvp, BxlPTraceForcedProcessNames, "");
        return newEnvp;
    }
    else
    {
        char **newEnvp = ensure_paths_included_in_env(envp, LD_PRELOAD_ENV_VAR_PREFIX, detoursLibFullPath_, NULL);
        if (newEnvp != envp)
        {
            LOG_DEBUG("envp has been modified with %s added to %s", detoursLibFullPath_, "LD_PRELOAD");
        }

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

        dir = real_opendir(currentDirectory.c_str());

        if (dir != NULL)
        {
            while ((ent = real_readdir(dir)) != NULL)
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

            real_closedir(dir);
        }
        else
        {
            // Something went wrong with opendir
            LOG_DEBUG("[BxlObserver::EnumerateDirectory] opendir failed on '%s' with errno %d\n", currentDirectory, errno);
            return false;
        }
    }

    return true;
}
