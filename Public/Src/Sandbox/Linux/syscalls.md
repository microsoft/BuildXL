# List of syscalls and their descriptions generated with
```bash
(
  for sym in $(nm --debug-syms -D /lib/x86_64-linux-gnu/libc.so.6 | cut -d\  -f3); do
    apropos $sym
  done
) 2>/dev/null | grep ' (2)' | sort | uniq
```

# Current Implementation Status

## Legend

| status                   | meaning                             |
| ------                   | -------                             |
|                          | no need to interpose                |
| :question:               | not clear whether needs interposing |
| :white_check_mark:       | already interposing                 |
| :heavy_exclamation_mark: | not interposing but SHOULD          |

## Status 

| status                   | syscall                    | apropos                                                             |
| ------------------------ | ---------------------      | ------------------------------------------------------------------- |
|                          | accept (2)                 | accept a connection on a socket                                     |
|                          | accept4 (2)                | accept a connection on a socket                                     |
|                          | add_key (2)                | add a key to the kernel's key management facility                   |
|                          | inotify_add_watch (2)      | add a watch to an initialized inotify instance                      |
|                          | fanotify_mark (2)          | add, remove, or modify an fanotify mark on a filesystem object      |
|                          | pkey_alloc (2)             | allocate or free a protection key                                   |
|                          | pkey_free (2)              | allocate or free a protection key                                   |
|                          | alloc_hugepages (2)        | allocate or free huge pages                                         |
|                          | free_hugepages (2)         | allocate or free huge pages                                         |
|                          | shmget (2)                 | allocates a System V shared memory segment                          |
|                          | signal (2)                 | ANSI C signal handling                                              |
|                          | flock (2)                  | apply or remove an advisory lock on an open file                    |
|                          | timer_gettime (2)          | arm/disarm and fetch state of POSIX per-process timer               |
|                          | timer_settime (2)          | arm/disarm and fetch state of POSIX per-process timer               |
|                          | bind (2)                   | bind a name to a socket                                             |
|                          | io_cancel (2)              | cancel an outstanding asynchronous I/O operation                    |
|                          | brk (2)                    | change data segment size                                            |
|                          | sbrk (2)                   | change data segment size                                            |
| :white_check_mark:       | utime (2)                  | change file last access and modification times                      |
| :white_check_mark:       | utimes (2)                 | change file last access and modification times                      |
| :white_check_mark:       | utimensat (2)              | change file timestamps with nanosecond precision                    |
|                          | iopl (2)                   | change I/O privilege level                                          |
| :white_check_mark:       | chown (2)                  | change ownership of a file                                          |
| :white_check_mark:       | chown32 (2)                | change ownership of a file                                          |
| :white_check_mark:       | fchown (2)                 | change ownership of a file                                          |
| :white_check_mark:       | fchown32 (2)               | change ownership of a file                                          |
| :white_check_mark:       | fchownat (2)               | change ownership of a file                                          |
| :white_check_mark:       | lchown (2)                 | change ownership of a file                                          |
| :white_check_mark:       | lchown32 (2)               | change ownership of a file                                          |
| :white_check_mark:       | chmod (2)                  | change permissions of a file                                        |
| :white_check_mark:       | fchmod (2)                 | change permissions of a file                                        |
| :white_check_mark:       | fchmodat (2)               | change permissions of a file                                        |
|                          | nice (2)                   | change process priority                                             |
| :question:               | chroot (2)                 | change root directory                                               |
| :white_check_mark:       | rename (2)                 | change the name or location of a file                               |
| :white_check_mark:       | renameat2 (2)              | change the name or location of a file                               |
| :white_check_mark:       | renameat (2)               | change the name or location of a file                               |
| :question:               | pivot_root (2)             | change the root filesystem                                          |
| :white_check_mark:       | futimesat (2)              | change timestamps of a file relative to a directory file descriptor |
|                          | chdir (2)                  | change working directory                                            |
|                          | fchdir (2)                 | change working directory                                            |
| :white_check_mark:       | access (2)                 | check user's permissions for a file                                 |
| :white_check_mark:       | faccessat (2)              | check user's permissions for a file                                 |
|                          | clock_getres (2)           | clock and time functions                                            |
|                          | clock_gettime (2)          | clock and time functions                                            |
|                          | clock_settime (2)          | clock and time functions                                            |
|                          | close (2)                  | close a file descriptor                                             |
|                          | sync (2)                   | commit filesystem caches to disk                                    |
|                          | syncfs (2)                 | commit filesystem caches to disk                                    |
|                          | kcmp (2)                   | compare two processes to determine if they share a kernel resource  |
|                          | ioctl (2)                  | control device                                                      |
|                          | epoll_ctl (2)              | control interface for an epoll file descriptor                      |
| :white_check_mark:       | copy_file_range (2)        | Copy a range of data from one file to another                       |
|                          | getunwind (2)              | copy the unwind data to caller's buffer                             |
| (only needed on ia64)    | __clone2 (2)               | create a child process                                              |
| :white_check_mark:       | clone (2)                  | create a child process                                              |
| :white_check_mark:       | fork (2)                   | create a child process                                              |
| :question:               | vfork (2)                  | create a child process and block parent                             |
| :white_check_mark:       | mkdir (2)                  | create a directory                                                  |
| :white_check_mark:       | mkdirat (2)                | create a directory                                                  |
|                          | signalfd (2)               | create a file descriptor for accepting signals                      |
|                          | signalfd4 (2)              | create a file descriptor for accepting signals                      |
|                          | eventfd2 (2)               | create a file descriptor for event notification                     |
|                          | eventfd (2)                | create a file descriptor for event notification                     |
|                          | ioctl_userfaultfd (2)      | create a file descriptor for handling page faults in user space     |
|                          | userfaultfd (2)            | create a file descriptor for handling page faults in user space     |
|                          | create_module (2)          | create a loadable module entry                                      |
|                          | memfd_create (2)           | create an anonymous file                                            |
|                          | io_setup (2)               | create an asynchronous I/O context                                  |
|                          | fanotify_init (2)          | create and initialize fanotify group                                |
|                          | socket (2)                 | create an endpoint for communication                                |
|                          | spu_create (2)             | create a new spu context                                            |
|                          | remap_file_pages (2)       | create a nonlinear file mapping                                     |
|                          | socketpair (2)             | create a pair of connected sockets                                  |
|                          | timer_create (2)           | create a POSIX per-process timer                                    |
| :white_check_mark:       | mknod (2)                  | create a special or ordinary file                                   |
| :white_check_mark:       | mknodat (2)                | create a special or ordinary file                                   |
|                          | pipe2 (2)                  | create pipe                                                         |
|                          | pipe (2)                   | create pipe                                                         |
|                          | setsid (2)                 | creates a session and sets the process group ID                     |
|                          | subpage_prot (2)           | define a subpage protection for an address range                    |
| :white_check_mark:       | rmdir (2)                  | delete a directory                                                  |
| :white_check_mark:       | unlink (2)                 | delete a name and possibly the file it refers to                    |
| :white_check_mark:       | unlinkat (2)               | delete a name and possibly the file it refers to                    |
|                          | timer_delete (2)           | delete a POSIX per-process timer                                    |
|                          | io_destroy (2)             | destroy an asynchronous I/O context                                 |
|                          | getcpu (2)                 | determine CPU and NUMA node on which the calling thread is running  |
|                          | mincore (2)                | determine whether pages are resident in memory                      |
|                          | unshare (2)                | disassociate parts of the process execution context                 |
|                          | dup2 (2)                   | duplicate a file descriptor                                         |
|                          | dup (2)                    | duplicate a file descriptor                                         |
|                          | dup3 (2)                   | duplicate a file descriptor                                         |
|                          | tee (2)                    | duplicating pipe content                                            |
|                          | s390_sthyi (2)             | emulate STHYI instruction                                           |
|                          | s390_runtime_instr (2)     | enable/disable s390 CPU run-time instrumentation                    |
|                          | vm86 (2)                   | enter virtual 8086 mode                                             |
|                          | vm86old (2)                | enter virtual 8086 mode                                             |
|                          | rt_sigaction (2)           | examine and change a signal action                                  |
|                          | sigaction (2)              | examine and change a signal action                                  |
|                          | rt_sigprocmask (2)         | examine and change blocked signals                                  |
|                          | sigprocmask (2)            | examine and change blocked signals                                  |
|                          | rt_sigpending (2)          | examine pending signals                                             |
|                          | sigpending (2)             | examine pending signals                                             |
|                          | spu_run (2)                | execute an SPU context                                              |
| :white_check_mark:       | execve (2)                 | execute program                                                     | 
| :white_check_mark:       | execveat (2)               | execute program relative to a directory file descriptor             |
|                          | exit_group (2)             | exit all threads in a process                                       |
|                          | futex (2)                  | fast user-space locking                                             |
|                          | cacheflush (2)             | flush contents of instruction and/or data cache                     |
|                          | getsockopt (2)             | get and set options on sockets                                      |
|                          | setsockopt (2)             | get and set options on sockets                                      |
|                          | msgget (2)                 | get a System V message queue identifier                             |
|                          | semget (2)                 | get a System V semaphore set identifier                             |
|                          | getcwd (2)                 | get current working directory                                       |
| :white_check_mark:       | getdents (2)               | get directory entries                                               |
| :white_check_mark:       | getdents64 (2)             | get directory entries                                               |
|                          | getdtablesize (2)          | get file descriptor table size                                      |
| :white_check_mark:       | fstat (2)                  | get file status                                                     |
| :white_check_mark:       | fstat64 (2)                | get file status                                                     |
| :white_check_mark:       | fstatat (2)                | get file status                                                     |
| :white_check_mark:       | fstatat64 (2)              | get file status                                                     |
| :white_check_mark:       | lstat (2)                  | get file status                                                     |
| :white_check_mark:       | lstat64 (2)                | get file status                                                     |
| :question:               | newfstatat (2)             | get file status                                                     |
| :question:               | oldfstat (2)               | get file status                                                     |
| :question:               | oldlstat (2)               | get file status                                                     |
| :question:               | oldstat (2)                | get file status                                                     |
| :white_check_mark:       | stat (2)                   | get file status                                                     |
| :white_check_mark:       | stat64 (2)                 | get file status                                                     |
| :white_check_mark:       | statx (2)                  | get file status (extended)                                          |
|                          | fstatfs (2)                | get filesystem statistics                                           |
|                          | fstatfs64 (2)              | get filesystem statistics                                           |
|                          | statfs (2)                 | get filesystem statistics                                           |
|                          | statfs64 (2)               | get filesystem statistics                                           |
|                          | statvfs (2)                | get filesystem statistics                                           |
|                          | fstatvfs (2)               | get filesystem statistics                                           |
|                          | ustat (2)                  | get filesystem statistics                                           |
|                          | sysfs (2)                  | get filesystem type information                                     |
|                          | getegid (2)                | get group identity                                                  |
|                          | getegid32 (2)              | get group identity                                                  |
|                          | getgid (2)                 | get group identity                                                  |
|                          | getgid32 (2)               | get group identity                                                  |
|                          | getpagesize (2)            | get memory page size                                                |
|                          | oldolduname (2)            | get name and information about current kernel                       |
|                          | olduname (2)               | get name and information about current kernel                       |
|                          | uname (2)                  | get name and information about current kernel                       |
|                          | getpeername (2)            | get name of connected peer socket                                   |
|                          | modify_ldt (2)             | get or set a per-process LDT entry                                  |
|                          | gethostid (2)              | get or set the unique identifier of the current host                |
|                          | sethostid (2)              | get or set the unique identifier of the current host                |
|                          | getcontext (2)             | get or set the user context                                         |
|                          | setcontext (2)             | get or set the user context                                         |
|                          | getitimer (2)              | get or set value of an interval timer                               |
|                          | setitimer (2)              | get or set value of an interval timer                               |
|                          | timer_getoverrun (2)       | get overrun count for a POSIX per-process timer                     |
|                          | getpid (2)                 | get process identification                                          |
|                          | getppid (2)                | get process identification                                          |
|                          | times (2)                  | get process times                                                   |
|                          | getresgid (2)              | get real, effective and saved user/group IDs                        |
|                          | getresgid32 (2)            | get real, effective and saved user/group IDs                        |
|                          | getresuid (2)              | get real, effective and saved user/group IDs                        |
|                          | getresuid32 (2)            | get real, effective and saved user/group IDs                        |
|                          | getrusage (2)              | get resource usage                                                  |
|                          | getsid (2)                 | get session ID                                                      |
|                          | gethostname (2)            | get/set hostname                                                    |
|                          | sethostname (2)            | get/set hostname                                                    |
|                          | ioprio_get (2)             | get/set I/O scheduling class and priority                           |
|                          | ioprio_set (2)             | get/set I/O scheduling class and priority                           |
|                          | get_robust_list (2)        | get/set list of robust futexes                                      |
|                          | set_robust_list (2)        | get/set list of robust futexes                                      |
|                          | getgroups (2)              | get/set list of supplementary group IDs                             |
|                          | getgroups32 (2)            | get/set list of supplementary group IDs                             |
|                          | setgroups (2)              | get/set list of supplementary group IDs                             |
|                          | setgroups32 (2)            | get/set list of supplementary group IDs                             |
|                          | mq_getsetattr (2)          | get/set message queue attributes                                    |
|                          | getdomainname (2)          | get/set NIS domain name                                             |
|                          | setdomainname (2)          | get/set NIS domain name                                             |
|                          | getpriority (2)            | get/set program scheduling priority                                 |
|                          | setpriority (2)            | get/set program scheduling priority                                 |
|                          | getrlimit (2)              | get/set resource limits                                             |
|                          | prlimit (2)                | get/set resource limits                                             |
|                          | prlimit64 (2)              | get/set resource limits                                             |
|                          | setrlimit (2)              | get/set resource limits                                             |
|                          | ugetrlimit (2)             | get/set resource limits                                             |
|                          | gettimeofday (2)           | get / set time                                                      |
|                          | settimeofday (2)           | get / set time                                                      |
|                          | getsockname (2)            | get socket name                                                     |
|                          | sched_get_priority_max (2) | get static priority range                                           |
|                          | sched_get_priority_min (2) | get static priority range                                           |
|                          | sched_rr_get_interval (2)  | get the SCHED_RR interval for the named process                     |
|                          | gettid (2)                 | get thread identification                                           |
|                          | time (2)                   | get time in seconds                                                 |
|                          | geteuid (2)                | get user identity                                                   |
|                          | geteuid32 (2)              | get user identity                                                   |
|                          | getuid (2)                 | get user identity                                                   |
|                          | getuid32 (2)               | get user identity                                                   |
|                          | madvise (2)                | give advice about use of memory                                     |
|                          | nanosleep (2)              | high-resolution sleep                                               |
|                          | clock_nanosleep (2)        | high-resolution sleep with specifiable clock                        |
| :question:               | syscall (2)                | indirect system call                                                |
|                          | inotify_init1 (2)          | initialize an inotify instance                                      |
|                          | inotify_init (2)           | initialize an inotify instance                                      |
|                          | connect (2)                | initiate a connection on a socket                                   |
|                          | readahead (2)              | initiate file readahead into page cache                             |
|                          | perfmonctl (2)             | interface to IA-64 performance monitoring unit                      |
|                          | intro (2)                  | introduction to system calls                                        |
|                          | _syscall (2)               | invoking a system call without library support (OBSOLETE)           |
|                          | ioctl_iflags (2)           | ioctl() operations for inode flags                                  |
|                          | ioctl_ns (2)               | ioctl() operations for Linux namespaces                             |
|                          | ioctl_console (2)          | ioctls for console terminal and virtual consoles                    |
|                          | ioctl_tty (2)              | ioctls for terminals and serial lines                               |
|                          | membarrier (2)             | issue memory barriers on a set of threads                           |
| :question:               | syscalls (2)               | Linux system calls                                                  |
|                          | listen (2)                 | listen for connections on a socket                                  |
|                          | ioctl_list (2)             | list of ioctl calls in Linux/i386 kernel                            |
|                          | finit_module (2)           | load a kernel module                                                |
|                          | init_module (2)            | load a kernel module                                                |
|                          | kexec_file_load (2)        | load a new kernel for later execution                               |
|                          | kexec_load (2)             | load a new kernel for later execution                               |
|                          | uselib (2)                 | load shared library                                                 |
|                          | mlock2 (2)                 | lock and unlock memory                                              |
|                          | mlock (2)                  | lock and unlock memory                                              |
|                          | mlockall (2)               | lock and unlock memory                                              |
|                          | munlock (2)                | lock and unlock memory                                              |
|                          | munlockall (2)             | lock and unlock memory                                              |
| :white_check_mark:       | link (2)                   | make a new name for a file                                          |
| :white_check_mark:       | linkat (2)                 | make a new name for a file                                          |
| :white_check_mark:       | symlink (2)                | make a new name for a file                                          |
| :white_check_mark:       | symlinkat (2)              | make a new name for a file                                          |
|                          | idle (2)                   | make process 0 idle                                                 |
|                          | quotactl (2)               | manipulate disk quotas                                              |
|                          | fcntl (2)                  | manipulate file descriptor                                          |
|                          | fcntl64 (2)                | manipulate file descriptor                                          |
|                          | fallocate (2)              | manipulate file space                                               |
|                          | keyctl (2)                 | manipulate the kernel's key management facility                     |
|                          | ioctl_fat (2)              | manipulating the FAT filesystem                                     |
|                          | sgetmask (2)               | manipulation of signal mask (obsolete)                              |
|                          | ssetmask (2)               | manipulation of signal mask (obsolete)                              |
|                          | mmap2 (2)                  | map files or devices into memory                                    |
|                          | mmap (2)                   | map or unmap files or devices into memory                           |
|                          | munmap (2)                 | map or unmap files or devices into memory                           |
|                          | mount (2)                  | mount filesystem                                                    |
|                          | migrate_pages (2)          | move all pages in a process to another set of nodes                 |
|                          | move_pages (2)             | move individual pages of a process to another node                  |
|                          | getrandom (2)              | obtain a series of random bytes                                     |
| :white_check_mark:       | name_to_handle_at (2)      | obtain handle for a pathname and open file via a handle             |
|                          | open_by_handle_at (2)      | obtain handle for a pathname and open file via a handle             |
|                          | mq_open (2)                | open a message queue                                                |
| :white_check_mark:       | creat (2)                  | open and possibly create a file                                     |
| :white_check_mark:       | open (2)                   | open and possibly create a file                                     |
| :white_check_mark:       | openat (2)                 | open and possibly create a file                                     |
|                          | epoll_create1 (2)          | open an epoll file descriptor                                       |
|                          | epoll_create (2)           | open an epoll file descriptor                                       |
|                          | seccomp (2)                | operate on Secure Computing state of the process                    |
|                          | prctl (2)                  | operations on a process                                             |
|                          | pciconfig_iobase (2)       | pci device information handling                                     |
|                          | pciconfig_read (2)         | pci device information handling                                     |
|                          | pciconfig_write (2)        | pci device information handling                                     |
|                          | bpf (2)                    | perform a command on an extended BPF map or program                 |
|                          | inb (2)                    | port I/O                                                            |
|                          | inb_p (2)                  | port I/O                                                            |
|                          | inl (2)                    | port I/O                                                            |
|                          | inl_p (2)                  | port I/O                                                            |
|                          | insb (2)                   | port I/O                                                            |
|                          | insl (2)                   | port I/O                                                            |
|                          | insw (2)                   | port I/O                                                            |
|                          | inw (2)                    | port I/O                                                            |
|                          | inw_p (2)                  | port I/O                                                            |
|                          | outb (2)                   | port I/O                                                            |
|                          | outb_p (2)                 | port I/O                                                            |
|                          | outl (2)                   | port I/O                                                            |
|                          | outl_p (2)                 | port I/O                                                            |
|                          | outsb (2)                  | port I/O                                                            |
|                          | outsl (2)                  | port I/O                                                            |
|                          | outsw (2)                  | port I/O                                                            |
|                          | outw (2)                   | port I/O                                                            |
|                          | outw_p (2)                 | port I/O                                                            |
|                          | arm_fadvise (2)            | predeclare an access pattern for file data                          |
|                          | arm_fadvise64_64 (2)       | predeclare an access pattern for file data                          |
|                          | fadvise64 (2)              | predeclare an access pattern for file data                          |
|                          | fadvise64_64 (2)           | predeclare an access pattern for file data                          |
|                          | posix_fadvise (2)          | predeclare an access pattern for file data                          |
| :question:               | ptrace (2)                 | process trace                                                       |
|                          | query_module (2)           | query the kernel for various bits pertaining to modules             |
|                          | rt_sigqueueinfo (2)        | queue a signal and data                                             |
|                          | rt_tgsigqueueinfo (2)      | queue a signal and data                                             |
|                          | sigqueue (2)               | queue a signal and data to a process                                |
|                          | syslog (2)                 | read and/or clear kernel message ring buffer; set console_loglevel  |
|                          | io_getevents (2)           | read asynchronous I/O events from the completion queue              |
|                          | readdir (2)                | read directory entry                                                |
|                          | read (2)                   | read from a file descriptor                                         |
|                          | pread (2)                  | read from or write to a file descriptor at a given offset           |
|                          | pread64 (2)                | read from or write to a file descriptor at a given offset           |
| :white_check_mark:       | pwrite (2)                 | read from or write to a file descriptor at a given offset           |
| :white_check_mark:       | pwrite64 (2)               | read from or write to a file descriptor at a given offset           |
|                          | preadv2 (2)                | read or write data into multiple buffers                            |
|                          | preadv (2)                 | read or write data into multiple buffers                            |
| :white_check_mark:       | pwritev2 (2)               | read or write data into multiple buffers                            |
| :white_check_mark:       | pwritev (2)                | read or write data into multiple buffers                            |
|                          | readv (2)                  | read or write data into multiple buffers                            |
| :white_check_mark:       | writev (2)                 | read or write data into multiple buffers                            |
| :white_check_mark:       | readlink (2)               | read value of a symbolic link                                       |
| :white_check_mark:       | readlinkat (2)             | read value of a symbolic link                                       |
|                          | _sysctl (2)                | read/write system parameters                                        |
|                          | sysctl (2)                 | read/write system parameters                                        |
|                          | setns (2)                  | reassociate thread with a namespace                                 |
|                          | reboot (2)                 | reboot or enable/disable Ctrl-Alt-Del                               |
|                          | mq_timedreceive (2)        | receive a message from a message queue                              |
|                          | recv (2)                   | receive a message from a socket                                     |
|                          | recvfrom (2)               | receive a message from a socket                                     |
|                          | recvmsg (2)                | receive a message from a socket                                     |
|                          | recvmmsg (2)               | receive multiple messages on a socket                               |
|                          | mq_notify (2)              | register for notification when a message is available               |
|                          | mremap (2)                 | remap a virtual memory address                                      |
|                          | mq_unlink (2)              | remove a message queue                                              |
|                          | inotify_rm_watch (2)       | remove an existing watch from an inotify instance                   |
|                          | _llseek (2)                | reposition read/write file offset                                   |
|                          | llseek (2)                 | reposition read/write file offset                                   |
|                          | lseek (2)                  | reposition read/write file offset                                   |
|                          | request_key (2)            | request a key from the kernel's key management facility             |
|                          | restart_syscall (2)        | restart a system call after interruption by a stop signal           |
|                          | get_kernel_syms (2)        | retrieve exported kernel and module symbols                         |
|                          | get_mempolicy (2)          | retrieve NUMA memory policy for a thread                            |
|                          | ioctl_getfsmap (2)         | retrieve the physical layout of the filesystem                      |
|                          | lookup_dcookie (2)         | return a directory entry's path                                     |
|                          | rt_sigreturn (2)           | return from signal handler and cleanup stack frame                  |
|                          | sigreturn (2)              | return from signal handler and cleanup stack frame                  |
|                          | sysinfo (2)                | return system information                                           |
|                          | send (2)                   | send a message on a socket                                          |
|                          | sendmsg (2)                | send a message on a socket                                          |
|                          | sendto (2)                 | send a message on a socket                                          |
|                          | mq_timedsend (2)           | send a message to a message queue                                   |
|                          | tgkill (2)                 | send a signal to a thread                                           |
|                          | tkill (2)                  | send a signal to a thread                                           |
|                          | sendmmsg (2)               | send multiple messages on a socket                                  |
|                          | kill (2)                   | send signal to a process                                            |
|                          | killpg (2)                 | send signal to a process group                                      |
|                          | get_thread_area (2)        | set a GDT entry for thread-local storage                            |
|                          | set_thread_area (2)        | set a GDT entry for thread-local storage                            |
|                          | alarm (2)                  | set an alarm clock for delivery of a signal                         |
|                          | sched_getaffinity (2)      | set and get a thread's CPU affinity mask                            |
|                          | sched_setaffinity (2)      | set and get a thread's CPU affinity mask                            |
|                          | sched_getparam (2)         | set and get scheduling parameters                                   |
|                          | sched_setparam (2)         | set and get scheduling parameters                                   |
|                          | sched_getattr (2)          | set and get scheduling policy and attributes                        |
|                          | sched_setattr (2)          | set and get scheduling policy and attributes                        |
|                          | sched_getscheduler (2)     | set and get scheduling policy/parameters                            |
|                          | sched_setscheduler (2)     | set and get scheduling policy/parameters                            |
|                          | sigaltstack (2)            | set and/or get signal stack context                                 |
|                          | arch_prctl (2)             | set architecture-specific thread state                              |
|                          | set_mempolicy (2)          | set default NUMA memory policy for a thread and its children        |
|                          | setegid (2)                | set effective user or group ID                                      |
|                          | seteuid (2)                | set effective user or group ID                                      |
|                          | umask (2)                  | set file mode creation mask                                         |
|                          | capget (2)                 | set/get capabilities of thread(s)                                   |
|                          | capset (2)                 | set/get capabilities of thread(s)                                   |
|                          | getpgid (2)                | set/get process group                                               |
|                          | getpgrp (2)                | set/get process group                                               |
|                          | setpgid (2)                | set/get process group                                               |
|                          | setpgrp (2)                | set/get process group                                               |
|                          | setgid (2)                 | set group identity                                                  |
|                          | setgid32 (2)               | set group identity                                                  |
|                          | setfsgid (2)               | set group identity used for filesystem checks                       |
|                          | setfsgid32 (2)             | set group identity used for filesystem checks                       |
|                          | mbind (2)                  | set memory policy for a memory range                                |
|                          | set_tid_address (2)        | set pointer to thread ID                                            |
|                          | ioperm (2)                 | set port input/output permissions                                   |
|                          | mprotect (2)               | set protection on a region of memory                                |
|                          | pkey_mprotect (2)          | set protection on a region of memory                                |
|                          | setregid (2)               | set real and/or effective user or group ID                          |
|                          | setregid32 (2)             | set real and/or effective user or group ID                          |
|                          | setreuid (2)               | set real and/or effective user or group ID                          |
|                          | setreuid32 (2)             | set real and/or effective user or group ID                          |
|                          | setresgid (2)              | set real, effective and saved user or group ID                      |
|                          | setresgid32 (2)            | set real, effective and saved user or group ID                      |
|                          | setresuid (2)              | set real, effective and saved user or group ID                      |
|                          | setresuid32 (2)            | set real, effective and saved user or group ID                      |
|                          | personality (2)            | set the process execution domain                                    |
|                          | stime (2)                  | set time                                                            |
|                          | setup (2)                  | setup devices and filesystems, mount root filesystem                |
|                          | perf_event_open (2)        | set up performance monitoring                                       |
|                          | setuid (2)                 | set user identity                                                   |
|                          | setuid32 (2)               | set user identity                                                   |
|                          | setfsuid (2)               | set user identity used for filesystem checks                        |
|                          | setfsuid32 (2)             | set user identity used for filesystem checks                        |
| :question:               | ioctl_ficlone (2)          | share some the data of one file with another file                   |
| :question:               | ioctl_ficlonerange (2)     | share some the data of one file with another file                   |
| :question:               | ioctl_fideduperange (2)    | share some the data of one file with another file                   |
|                          | shutdown (2)               | shut down part of a full-duplex connection                          |
|                          | socketcall (2)             | socket system calls                                                 |
|                          | splice (2)                 | splice data to/from a pipe                                          |
|                          | vmsplice (2)               | splice user pages into a pipe                                       |
|                          | bdflush (2)                | start, flush, or tune buffer-dirty-flush daemon                     |
|                          | swapoff (2)                | start/stop swapping to file/device                                  |
|                          | swapon (2)                 | start/stop swapping to file/device                                  |
|                          | io_submit (2)              | submit asynchronous I/O blocks for processing                       |
|                          | acct (2)                   | switch process accounting on or off                                 |
|                          | arm_sync_file_range (2)    | sync a file segment with disk                                       |
|                          | sync_file_range2 (2)       | sync a file segment with disk                                       |
|                          | sync_file_range (2)        | sync a file segment with disk                                       |
|                          | fdatasync (2)              | synchronize a file's in-core state with storage device              |
|                          | fsync (2)                  | synchronize a file's in-core state with storage device              |
|                          | msync (2)                  | synchronize a file with a memory map                                |
|                          | _newselect (2)             | synchronous I/O multiplexing                                        |
|                          | pselect (2)                | synchronous I/O multiplexing                                        |
|                          | pselect6 (2)               | synchronous I/O multiplexing                                        |
|                          | select (2)                 | synchronous I/O multiplexing                                        |
|                          | select_tut (2)             | synchronous I/O multiplexing                                        |
|                          | rt_sigtimedwait (2)        | synchronously wait for queued signals                               |
|                          | sigtimedwait (2)           | synchronously wait for queued signals                               |
|                          | sigwaitinfo (2)            | synchronously wait for queued signals                               |
|                          | nfsservctl (2)             | syscall interface to kernel nfs daemon                              |
|                          | ipc (2)                    | System V IPC system calls                                           |
|                          | msgctl (2)                 | System V message control operations                                 |
|                          | msgop (2)                  | System V message queue operations                                   |
|                          | msgrcv (2)                 | System V message queue operations                                   |
|                          | msgsnd (2)                 | System V message queue operations                                   |
|                          | semctl (2)                 | System V semaphore control operations                               |
|                          | semop (2)                  | System V semaphore operations                                       |
|                          | semtimedop (2)             | System V semaphore operations                                       |
|                          | shmctl (2)                 | System V shared memory control                                      |
|                          | shmat (2)                  | System V shared memory operations                                   |
|                          | shmdt (2)                  | System V shared memory operations                                   |
|                          | shmop (2)                  | System V shared memory operations                                   |
|                          | _exit (2)                  | terminate the calling process                                       |
|                          | exit (2)                   | terminate the calling process                                       |
|                          | _Exit (2)                  | terminate the calling process                                       |
|                          | timerfd_create (2)         | timers that notify via file descriptors                             |
|                          | timerfd_gettime (2)        | timers that notify via file descriptors                             |
|                          | timerfd_settime (2)        | timers that notify via file descriptors                             |
| :white_check_mark:       | sendfile (2)               | transfer data between file descriptors                              |
| :white_check_mark:       | sendfile64 (2)             | transfer data between file descriptors                              |
|                          | process_vm_readv (2)       | transfer data between process address spaces                        |
|                          | process_vm_writev (2)      | transfer data between process address spaces                        |
|                          | s390_pci_mmio_read (2)     | transfer data to/from PCI MMIO memory page                          |
|                          | s390_pci_mmio_write (2)    | transfer data to/from PCI MMIO memory page                          |
| :white_check_mark:       | ftruncate (2)              | truncate a file to a specified length                               |
| :white_check_mark:       | ftruncate64 (2)            | truncate a file to a specified length                               |
| :white_check_mark:       | truncate (2)               | truncate a file to a specified length                               |
| :white_check_mark:       | truncate64 (2)             | truncate a file to a specified length                               |
|                          | adjtimex (2)               | tune kernel clock                                                   |
|                          | afs_syscall (2)            | unimplemented system calls                                          |
|                          | break (2)                  | unimplemented system calls                                          |
|                          | fattach (2)                | unimplemented system calls                                          |
|                          | fdetach (2)                | unimplemented system calls                                          |
|                          | getmsg (2)                 | unimplemented system calls                                          |
|                          | getpmsg (2)                | unimplemented system calls                                          |
|                          | gtty (2)                   | unimplemented system calls                                          |
|                          | isastream (2)              | unimplemented system calls                                          |
|                          | lock (2)                   | unimplemented system calls                                          |
|                          | madvise1 (2)               | unimplemented system calls                                          |
|                          | mpx (2)                    | unimplemented system calls                                          |
|                          | phys (2)                   | unimplemented system calls                                          |
|                          | prof (2)                   | unimplemented system calls                                          |
|                          | putmsg (2)                 | unimplemented system calls                                          |
|                          | putpmsg (2)                | unimplemented system calls                                          |
|                          | security (2)               | unimplemented system calls                                          |
|                          | stty (2)                   | unimplemented system calls                                          |
|                          | tuxcall (2)                | unimplemented system calls                                          |
|                          | unimplemented (2)          | unimplemented system calls                                          |
|                          | vserver (2)                | unimplemented system calls                                          |
|                          | delete_module (2)          | unload a kernel module                                              |
|                          | umount2 (2)                | unmount filesystem                                                  |
|                          | umount (2)                 | unmount filesystem                                                  |
|                          | vhangup (2)                | virtually hangup the current terminal                               |
|                          | epoll_pwait (2)            | wait for an I/O event on an epoll file descriptor                   |
|                          | epoll_wait (2)             | wait for an I/O event on an epoll file descriptor                   |
|                          | rt_sigsuspend (2)          | wait for a signal                                                   |
|                          | sigsuspend (2)             | wait for a signal                                                   |
|                          | wait (2)                   | wait for process to change state                                    |
|                          | waitid (2)                 | wait for process to change state                                    |
|                          | waitpid (2)                | wait for process to change state                                    |
|                          | wait3 (2)                  | wait for process to change state, BSD style                         |
|                          | wait4 (2)                  | wait for process to change state, BSD style                         |
|                          | pause (2)                  | wait for signal                                                     |
|                          | poll (2)                   | wait for some event on a file descriptor                            |
|                          | ppoll (2)                  | wait for some event on a file descriptor                            |
| :white_check_mark:       | write (2)                  | write to a file descriptor                                          |
|                          | sched_yield (2)            | yield the processor                                                 |
