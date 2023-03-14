# Linux PTrace Sandbox

Source code: `Public/Src/Sandbox/Linux/PTraceSandbox.[ch]pp`, `Public/Src/Sandbox/Linux/ptracedaemon.cpp`, `Public/Src/Sandbox/Linux/ptracerunner.cpp`

## Background
The ptrace based sandbox exists due the Linux function interpose based sandbox not being able to detect file accesses from binaries that statically link libc.
Therefore, the Linux sandbox will first detect whether a binary is statically linked using objdump whenever it gets a call to exec.
If statically linked, then the ptrace sandbox will be used, and the binary being executed will then be run on a forked process with `PTRACE_TRACEME` set.

## Overview
The ptrace based sandbox works in the following way:
1. Whenever exec is called, detect whether a binary that is being executed statically links libc by using objdump. This should always work because the BuildXL sandbox will execute the parent process through a bash script, and /bin/bash does not statically link libc.
2. If the binary is statically linked, the `PTraceSandbox` class is used to run the binary with ptrace attached.
3. The tracer process will be spawned, while the main process will execute the binary using exec.
4. The child process will run `PTraceSandbox::ExecuteWithPTraceSandbox` which will create a seccomp filter, set `prctl(PR_SET_PTRACER, PR_SET_PTRACER_ANY)` to allow it to be traced, and finally execute the binary.
5. The tracer process will run `PTraceSandbox::AttachToProcess`. This function will have the main loop which will pause the main process until it is signalled by the tracee.
6. When the tracer is invoked, the syscall number can be found by reading `ORIG_RAX`.
7. Based on the syscall number, the appropriate handler function is invoked to report the access.
8. Arguments can be read by reading the values of the stack on the appropriate registers. Get the address of the register using `PTraceSandbox::GetArgumentAddr`, and call `PTraceSandbox::ReadArgumentString` for a string argument or `PTraceSandbox::ReadArgumentLong` for an integer argument.

## Daemon process
- When a statically linked binary is detected, the executing process will send a message to the daemon process.
- The daemon process will spawn a runner process which will become the tracer for the statically linked process.
- When the statically linked process finishes execution and the tracer dies, the daemon will call waitpid on it to ensure that it does not turn into a zombie process.

## Notes
### Reading string arguments
- String arguments can only be read 8 bytes at a time. Some arguments may not be null terminated, verify whether this is the case with the man page and ensure it is properly handled when calling `PTraceSandbox::ReadArgumentString`.

## Resources
 - man pages: https://man7.org/linux/man-pages/man2/ptrace.2.html, https://man7.org/linux/man-pages/man2/seccomp.2.html
 - strace readme: https://github.com/strace/strace/blob/master/doc/README-linux-ptrace
 - Basic Tracing example: https://www.alfonsobeato.net/c/modifying-system-call-arguments-with-ptrace/
 - Filtering with seccomp: https://outflux.net/teach-seccomp/, https://www.alfonsobeato.net/c/filter-and-modify-system-calls-with-seccomp-and-ptrace/