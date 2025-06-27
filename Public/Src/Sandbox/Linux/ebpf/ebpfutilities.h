// Copyright (c) Microsoft Corporation
// SPDX-License-Identifier: GPL-2.0 OR MIT

#ifndef __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H
#define __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H

#include "bpf/bpf_helpers.h"

#include "vmlinux.h"
#include "kernelconstants.h"
#include "ebpfstringutilities.h"
#include "ebpfcommon.h"

extern int LINUX_KERNEL_VERSION __kconfig;

/*
 * Map containing currently active process id -> runner pid. Root process id is pre-populated by the userspace.
 * Observe these pids are the ones corresponding to the root namespace. So the assumption is that
 * BuildXL is running in the root namespace, otherwise pids won't match.
 * TODO: Ideally we should always return pids corresponding with the same namespace where BuildXL was launched
 * (which in an arbitrary situation might not be the root one)
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, pid_t);
    // The value is the pid of the runner that this sandboxed process belongs to
    __type(value, pid_t);
    // This is the max value of concurrent processes that can run in a linux OS
    // We will probably be always very far from reaching this number, but at the same time this map is pretty lightweight (int -> long)
    // so this shouldn't have a big memory footprint
    __uint(max_entries, 4194304);
    // We don't want to preallocate memory for this map, as the max bound is way higher than the usual number of processes that will run concurrently
    __uint(map_flags, BPF_F_NO_PREALLOC);
    // We need to share the pid_map across all runners
    __uint(pinning, LIBBPF_PIN_BY_NAME);
} pid_map SEC(".maps");

/**
 * Additional options to pass to the sandbox per pip.
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    // The key is the pid of the runner process that this sandboxed process belongs to
    __type(key, pid_t);
    __type(value, sandbox_options);
    // We need to share the options across all runners
    __uint(pinning, LIBBPF_PIN_BY_NAME);
} sandbox_options_per_pip SEC(".maps");

/*
 * Ring buffer used to communicate file accesses to userspace.
 * We have one of these per runner pid and is held in the map-of-maps file_access_per_pip
 * This map is dynamically created in user space when a pip is about to start
 */
struct file_access_ring_buffer {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, FILE_ACCESS_RINGBUFFER_SIZE);
} file_access_ring_buffer SEC(".maps");

/**
 * Ring buffer used to send debug events to the userspace. Same per-pip settings as the above map
  */
struct debug_ring_buffer {
    __uint(type, BPF_MAP_TYPE_RINGBUF);
    __uint(max_entries, DEBUG_RINGBUFFER_SIZE);
} debug_ring_buffer SEC(".maps");

/**
 * This is a map of maps where the key is a PID of the runner associated with this sandboxed process
 * and its value is the file access ring buffer associated with each pip.
 * We need one file access ring buffer per pip (that is, per runner) since each runner
 * subscribes to its own file access ring buffer and applies file manifest specific logic
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Each entry in this map is a ring buffer - significantly big in theory - so we don't want to preallocate memory for it
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct file_access_ring_buffer);
} file_access_per_pip SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one debug ring buffer per sandboxed process.
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Each entry in this map is a ring buffer - significantly big in theory - so we don't want to preallocate memory for it
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct debug_ring_buffer);
} debug_buffer_per_pip SEC(".maps");

/**
 * Used to hold processes that will breakaway from the sandbox.
 * Used on kernel side only, this map is not exposed to user side
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH);
    __type(key, pid_t);
    __type(value, pid_t);
    // The upper bound represents the max number of pids that matches a breakaway definition from the time
    // execve is traced to the point where wakeup_new_task happens. So in theory we should be fine with a pretty
    // small number, but considering this is an int -> int map, memory shouldn't be a big concern
    __uint(max_entries, 512);
} breakaway_pids SEC(".maps");

/**
 * Map containing breakaway processes populated from the user side. We have one of this per pip and are held in a 
 * map of maps
 */
struct breakaway_processes {
    __uint(type, BPF_MAP_TYPE_ARRAY);
    __uint(key_size, sizeof(pid_t));
    __uint(value_size, sizeof(breakaway_process));
    __uint(max_entries, MAX_BREAKAWAY_PROCESSES);
} breakaway_processes SEC(".maps");

/**
 * Similar to file_access_per_pip, holds one maps of breakaway processes per sandboxed process.
 * We set max entries dynamically at creation time
 */
struct {
    __uint(type, BPF_MAP_TYPE_HASH_OF_MAPS);
    __type(key, pid_t);
    // We need all runners to share this map
    __uint(pinning, LIBBPF_PIN_BY_NAME);
    // The max number of entries is the max number of runners that can run concurrently, which is typically way over dimensioned
    // Most pips won't have breakaway processes, so we set the map flags to avoid preallocating memory
    __uint(map_flags, BPF_F_NO_PREALLOC);
    __array(values, struct breakaway_processes);
} breakaway_processes_per_pip SEC(".maps");

// Whether the given pid is one we care about (i.e. is part of the pid map we keep). If the pid is valid,
// sets runner_pid to its associated value
__attribute__((always_inline)) static bool is_valid_pid(const pid_t pid, pid_t *runner_pid) {
    pid_t *value = bpf_map_lookup_elem(&pid_map, &pid);
    if (value)
    {
        *runner_pid = *value;
        return true;
    }

    return false;
}

__attribute__((always_inline)) static inline bool monitor_process(const pid_t pid, pid_t runner_pid) {
    struct sandbox_options *options = bpf_map_lookup_elem(&sandbox_options_per_pip, &runner_pid);

    // If for some reason options was not set, assume that child processes are monitored
    if (!options) {
        return true;
    }

    // If this is not the root process, then this flag is not applicable
    // We will always monitor child processes of children
    if (pid == options->root_pid) {
        // Only the first exec on the root process is counted against the monitoring child processes flag
        // If another exec comes on the same process, we will not monitor child processes if the flag set.
        if (!options->root_pid_init_exec_occured) {
            options->root_pid_init_exec_occured = 1;
            return true;
        }
    }

    return options->is_monitoring_child_processes;
}

// Returns the parent pid of the current task
__attribute__((always_inline)) static int get_ppid() {
    struct task_struct *current = (struct task_struct*)bpf_get_current_task();
    struct task_struct *parent;
    int ppid;

    bpf_core_read(&parent, sizeof(parent), &current->real_parent);
    bpf_core_read(&ppid, sizeof(ppid), &parent->tgid);

    return ppid;
}

// Used to store temporary paths. We need 2 entries for the case of exec and rename, which require dealing with two paths
// simultaneously
struct
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    // using PATH_MAX * 2 to keep the verifier happy.
    __uint(value_size, PATH_MAX * 2);
    __uint(max_entries, 2);
} tmp_paths SEC(".maps");

// Useful for retrieving the couple of available temporary paths from tmp_paths
const static int ZERO = 0;
const static int ONE = 1;

// We use one entry per cpu
// Used by deref_path_info, combine_paths, and argv_to_string
// The dereference needs two paths, so the size here is PATH_MAX * 2, and the resulting element is logically split in halves
// More generally, it is very useful to use a PATH_MAX * 2 sized buffer for path-related operations (when two paths are involved, or when 
// a temporary path is kept while operating with another one), as the verifier will be happy with the given boundaries
struct
{
    __uint(type, BPF_MAP_TYPE_PERCPU_ARRAY);
    __uint(key_size, sizeof(uint32_t));
    __uint(value_size, PATH_MAX * 2);
    __uint(max_entries, 1);
} derefpaths SEC(".maps");

/**
 * Body of the loop used in deref_path_info.
 * This is used instead of defining it directly in the function because depending on the current kernel version
 * different types of loops must be used.
 * 
 * Returns 0 if the loop should continue, 1 if it should return, and 2 if it should break.
 * If the return value is set to 1 then the return code is set in the returncode variable.
 */
__attribute__((always_inline)) static inline int deref_paths_info_loop(
    unsigned int i,
    int *returncode,
    const void **dentry,
    const void **newdentry,
    const void *vfsmount,
    const void **mnt,
    char **dname,
    char *temp,
    int *dlen,
    int *dlen2,
    unsigned int *psize,
    uint32_t *ptsize)
{
    *dname = (char *)BPF_CORE_READ((struct dentry *)*dentry, d_name.name);

    if (!*dname)
    {
        // If we didn't have a mount set, this means we reach the root of the filesystem
        if (vfsmount == NULL)
        {
            return 2; // break
        }

        *returncode = 0;
        return 1; // return
    }
    // store this dentry name in start of second half of our temporary storage
    *dlen = bpf_core_read_str(&temp[PATH_MAX], PATH_MAX, *dname);

    // get parent dentry
    *newdentry = (char *)BPF_CORE_READ((struct dentry *)*dentry, d_parent);
    
    // Check if the retrieved dname is just a '/'. In that case, we just want to skip it.
    // We will consistently add separators in between afterwards, so we don't want a double slash
    if (!(temp[PATH_MAX] == '/' && *dlen == 2))
    {
        // NOTE: We copy the value of these variables to local variables and then back to the original pointers
        // because the asm code we execute below failed to compile when using the pointers directly.
        int size = *psize;
        int tsize = *ptsize;

        // copy the temporary copy to the first half of our temporary storage, building it backwards from the middle of
        // it
        *dlen2 = bpf_core_read_str(&temp[(PATH_MAX - size - *dlen) & (PATH_MAX - 1)], *dlen & (PATH_MAX - 1), &temp[PATH_MAX]);
        // check if current dentry name is valid
        if (*dlen2 <= 0 || *dlen <= 0 || *dlen >= PATH_MAX || size + *dlen > PATH_MAX)
        {
            *returncode = 0;
            return 1; // return
        }

        if (size > 0)
        {
            asm volatile("%[tsize] = " XSTR(PATH_MAX) "\n"
                        "%[tsize] -= %[size]\n"
                        "%[tsize] -= 1\n"
                        "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                        : [size] "+&r"(size), [tsize] "+&r"(tsize)
                        );

            temp[tsize & (PATH_MAX - 1)] = '/';
        }

        size = (size + *dlen2) &
            (PATH_MAX - 1); // by restricting size to PATH_MAX we help the verifier keep the complexity
                            // low enough so that it can analyse the loop without hitting the 1M ceiling

        // Copy back modifications to size and tsize
        *psize = size;
        *ptsize = tsize;
    }

    // check if this is the root of the filesystem or we reach the given mountpoint
    // We always prefer the mountpoint instead of continuing walking up the chain so we honor what the application context
    // is trying to do wrt path lookups
    if (!*newdentry || *dentry == *newdentry || *newdentry == BPF_CORE_READ((struct vfsmount *)vfsmount, mnt_root))
    {
        // check if we're on a mounted partition
        // find mount struct from vfsmount
        const void *parent = BPF_CORE_READ((struct mount *)*mnt, mnt_parent);

        // check if we're at the real root
        if (parent == *mnt)
        {
            return 2; // break
        }

        // move to mount point
        *newdentry = BPF_CORE_READ((struct mount *)*mnt, mnt_mountpoint);
        *mnt = parent;

        // another check for real root
        if (*dentry == *newdentry)
        {
            return 2; // break
        }
    }

    // go up one directory
    *dentry = *newdentry;

    return 0; // continue
}

// Returns a string representation of the content of a struct path (dentry and vfsmount being its two components)
__attribute__((always_inline)) static inline uint32_t deref_path_info(char *dest, const void *dentry, const void *vfsmount)
{
    int dlen, dlen2;
    char *dname = NULL;
    char *temp = NULL;
    unsigned int i;
    unsigned int size = 0;
    const void *path = NULL;
    const void *newdentry = NULL;
    const void *mnt = NULL;
    uint32_t tsize = 0;

    // nullify string in case of error
    dest[0] = 0x00;
 
    mnt = container_of(vfsmount, struct mount, mnt);
 
    // retrieve temporary filepath storage
    int index = 0;
    temp = bpf_map_lookup_elem(&derefpaths, &index);
    if (!temp)
    {
        return 0;
    }
 
    int returncode = 0;
    if (LINUX_KERNEL_VERSION >= KERNEL_VERSION(6, 8, 0))
    {
        // Newer kernels hit the instruction limit when using bpf_for, so we use a regular for loop
        // to avoid hitting the limit.
        for (i = 0; i < FILEPATH_NUMDIRS; i++)
        {
            int loopexit = deref_paths_info_loop(
                i,
                &returncode,
                &dentry,
                &newdentry,
                vfsmount,
                &mnt,
                &dname,
                temp,
                &dlen,
                &dlen2,
                &size,
                &tsize);

            // loopexit can be:
            // 0: continue the loop
            // 1: return with the exit code set in returncode
            // 2: break
            if (loopexit == 1)
            {
                return returncode;
            }
            else if (loopexit == 2)
            {
                break;
            }
        }
    }
    else
    {
        bpf_for(i, 0, FILEPATH_NUMDIRS)
        {
            int loopexit = deref_paths_info_loop(
                i,
                &returncode,
                &dentry,
                &newdentry,
                vfsmount,
                &mnt,
                &dname,
                temp,
                &dlen,
                &dlen2,
                &size,
                &tsize);

            // loopexit can be:
            // 0: continue the loop
            // 1: return with the exit code set in returncode
            // 2: break
            if (loopexit == 1)
            {
                return returncode;
            }
            else if (loopexit == 2)
            {
                break;
            }
        }
    }
    
    // check if we exhausted the number of directories we can traverse
    if (i == FILEPATH_NUMDIRS)
    {
        // add a '+/' to the start to indicate it's not a full path
 
        // Following piece of asm is required as clang likes to optimise
        // an increment followed by ANDing with (PATH_MAX -1), into simply
        // XORing with (PATH_MAX -1) and then converting to 32 bits by
        // <<32, >>32. This means the verifier thinks max value is 2^32 -1,
        // instead of (PATH_MAX -1).
 
        asm volatile("%[size] += 1\n"
                     "%[tsize] = " XSTR(PATH_MAX) "\n"
                     "%[tsize] -= %[size]\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '/';
 
        asm volatile("%[size] += 1\n"
                     "%[tsize] -= 1\n"
                     "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                    : [size] "+&r"(size), [tsize] "+&r"(tsize)
                    );
        temp[tsize & (PATH_MAX - 1)] = '+';
    }
    else if (size == 0)
    {
        // This means we only found '/' characters along the way. Assume this represents the root dir
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '\0';
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '/';
    }
    else if (size == 1)
    {
        // This means the shortest valid read would be a single null character.
        // assume this represents the root dir
        size++;
        temp[(PATH_MAX - size) & (PATH_MAX -1)] = '/';
    }
    else if (size > 2)
    {
        // size of 2 is simply "/" which is good. Need to check >2.
 
        // check if starting with '/'
        if (temp[(PATH_MAX - size) & (PATH_MAX - 1)] == '/')
        {
            // check for double / ("//")
            if (temp[(PATH_MAX - (size - 1)) & (PATH_MAX - 1)] == '/')
            {
                size--;
            }
        }
        else
        {
            // add a '/'
 
            asm volatile("%[size] += 1\n"
                         "%[tsize] = " XSTR(PATH_MAX) "\n"
                         "%[tsize] -= %[size]\n"
                         "%[tsize] &= " XSTR(PATH_MAX - 1) "\n"
                        : [size] "+&r"(size), [tsize] "+&r"(tsize)
                        );
 
            temp[tsize & (PATH_MAX - 1)] = '/';
        }
    }
 
    // copy the path from the temporary location to the destination
    dlen = bpf_core_read_str(dest, PATH_MAX, &temp[(PATH_MAX - size) & (PATH_MAX -1)]);

    if (dlen <= 0)
    {
        return 0;
    }

    return dlen;
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode(const struct dentry *dentry) {
    return BPF_CORE_READ(dentry, d_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_file(const struct file *file) {
    return BPF_CORE_READ(file, f_inode, i_mode);
}

// Returns the mode stored in the corresponding inode
__attribute__((always_inline)) static inline unsigned int get_mode_from_path(const struct path *path) {
    return get_mode(BPF_CORE_READ(path, dentry));
}

// Turns a struct path into a string representation of the full path
__attribute__((always_inline)) static inline uint32_t path_to_string(char *dest, const struct path* path)
{
    if (!dest)
    {
        return 0;
    }

    dest[0] = '\0';

    void *dentry = BPF_CORE_READ(path, dentry);
    if (!dentry)
    {
        return 0;
    }

    // Observe de-referencing can work even if the mount is missing, so no null checking here.
    void *vfsmount = BPF_CORE_READ(path, mnt);
    
    return deref_path_info(dest, dentry, vfsmount);
}

__attribute__((always_inline)) static inline uint32_t fd_to_string(char *fdPath, int fd, const void *task)
{
    void *path = NULL;
    
    // check if fd is valid
    int maxFds = BPF_CORE_READ((struct task_struct *)task, files, fdt, max_fds);
    if (fd < 0 || fd > MAX_FDS || maxFds <= 0 || fd > maxFds) 
    {
        return 0;
    }

    // Retrieve the file descriptor table from the current task
    const void **fdTable = (const void **)BPF_CORE_READ((struct task_struct *)task, files, fdt, fd);
    if (!fdTable)
    {
        return 0;
    }

    // Retrieve the struct file instance that is pointed by the fd
    const struct file *fdfile = NULL;
    if (bpf_core_read(&fdfile, sizeof(fdfile), &fdTable[fd & MAX_FDS]) != READ_OKAY || !fdfile)
    {
        return 0;
    }
    else
    {
        // Resolve the corresponding struct path to a string
        struct path path = BPF_CORE_READ(fdfile, f_path);
        return path_to_string(fdPath, &path);
    }
}

// Combines path and atom, placing the result in path
__attribute__((always_inline)) static inline uint32_t combine_paths(char* path, const char* atom)
{
    uint32_t tsize = 0;

    char *temp_path = NULL;
    int index = 0;
    temp_path = bpf_map_lookup_elem(&derefpaths, &index);
    if (!temp_path)
    {
        return 0;
    }

    // Copy the path to the temporary path. Observe the temporary path has size 2*PATH_MAX, so the
    // verifier will allow putting two paths together without complaining
    int length = bpf_core_read_str(temp_path, PATH_MAX, path);

    // Check whether the last element of the path is already a directory separator, and add one otherwise.
    // Observe that length includes the null character, so 'length - 1' should be pointing to the null separator
    if (temp_path[(length - 2) & (PATH_MAX - 1)] != '/')
    {
        temp_path[(length - 1) & (PATH_MAX - 1)] = '/';
    }
    else
    {
        length--;
    }

    bpf_core_read_str(&temp_path[length & (PATH_MAX - 1)], PATH_MAX, atom);

    // Copy to the final destination
    return bpf_core_read_str(path, PATH_MAX, temp_path);
}

// Returns the current working directory of the given task
__attribute__((always_inline)) static inline uint32_t get_cwd(struct task_struct * task, char* cwd)
{
    struct path pwd_path = BPF_CORE_READ(task, fs, pwd);
    return path_to_string(cwd, &pwd_path);
}

// Returns a string representation of the path carried by file descriptor followed but a filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_string_to_string(char* path, int fd, const char* filename, bool user_strings)
{
    // Copy the filename to the destination, as a way to bound it to PATH_MAX and keep the verifier happy
    int length = user_strings
        ? bpf_core_read_user_str(path, PATH_MAX, filename)
        : bpf_core_read_str(path, PATH_MAX, filename);

    if (length <= 0)
    {
        return 0;
    }
    
    // Check if file descriptor is invalid or if the filename is absolute. In those case, the file descriptor is ignored and
    // the final path should be in the filename
    if ((fd < 0 && fd != AT_FDCWD) || path[0] == '/')
    {
        return length;
    }

    // The file descriptor is valid. This could either be AT_FDCWD (the current directory) or a valid handle
    struct task_struct *task = (struct task_struct *)bpf_get_current_task();
    if (fd == AT_FDCWD)
    {
        length = get_cwd(task, path);
    }
    else
    {
        length = fd_to_string(path, fd, task);
    }

    if (length <= 0)
    {        
        return 0;
    }

    // We got a resolved directory in path and a relative path on filename. Put them together.
    return combine_paths(path, filename);
}

// Returns a string representation of the path carried by file descriptor followed by a struct filename.
// These input arguments are used to perform a path lookup, which means that the dentry/inode is not resolved
// yet
__attribute__((always_inline)) static inline uint32_t fd_filename_to_string(char* output_path, int fd, const struct filename* filename_struct) {
    const char *filename = BPF_CORE_READ(filename_struct, name);

    return fd_string_to_string(output_path, fd, filename, /* user_strings */ false);
}

// Returns a string representation of the path carried by a nameidata instance.
// Observe that nameidata is typically used to perform a path lookup, which means that the dentry/inode is not resolved
// yet (and might not even exist in case of an absent path). The consequence of this is that we have to do extra work to
// put the final path together
__attribute__((always_inline)) static inline uint32_t nameidata_to_string(char* path, const struct nameidata* ns)
{
    // A nameidata contains a file descriptor maybe pointing to a directory (dfd) and a name component which may contain a filename 
    // or full path 
    int fd = BPF_CORE_READ(ns, dfd);
    const struct filename *filename = BPF_CORE_READ(ns, name);

    return fd_filename_to_string(path, fd, filename);
}

// Returns the path to the current executable by inspecting the given task
__attribute__((always_inline)) static inline unsigned int get_task_exec_path(struct task_struct* task, char* dest)
{
    dest[0] = '\0';

    struct mm_struct* mm = BPF_CORE_READ(task, mm);
    if (mm)
    {
        struct path path = BPF_CORE_READ(mm, exe_file, f_path);
        return path_to_string(dest, &path);
    }
    
    return 0;
}

/**
 * Returns whether the mode is set and it is not a file, nor a directory neither a symlink
 */
__attribute__((always_inline)) static inline bool is_non_file(mode_t mode)
{
    return mode != 0 && !S_ISDIR(mode) && !S_ISREG(mode) && !S_ISLNK(mode);
}

/**
 * argv_to_string() - Converts an argv array to a string representation.
 * @argv: The argv array to convert. This pointer is in user memory.
 * @dest: The destination buffer to store the resulting string. This pointer is in kernel memory.
 * 
 * Each argument will be separated by a space.
 * Final string is null terminated.
 * NOTE: the destination buffer is assumed to always be PATH_MAX in size.
 */
static int argv_to_string(char *const *argv, char* dest)
{
    if (!argv) {
        return 0;
    }
    
    int index = 0;
    int remaining_length = PATH_MAX - 1;

    // Using temporary path storage here to read each argument
    char *temp = bpf_map_lookup_elem(&derefpaths, &index);
    if (temp == NULL) {
        return 0;
    }
    
    for (int i = 0; i < MAX_ARGV_ARGUMENTS; i++) {
        char *arg;
        // Get a pointer to the current argument
        if (bpf_probe_read_user(&arg, sizeof(arg), &argv[i]) != 0) {
            break;
        }
        
        // Copy string to temporary location starting on the second half of the string
        long copied_len = bpf_probe_read_user_str(&temp[PATH_MAX], PATH_MAX, arg);
        if (copied_len <= 0) {
            break;
        }

        // Copy the string to the first half of the temporary array to concatenate it with the rest of the arguments
        // We'll add a space here if it's the second argument onwards
        if (i > 0) {
            temp[index & (PATH_MAX - 1)] = ' ';
            index++;
        }
        
        // NOTE: this is a kernel str because we are copying from the derefpaths map now
        long copied_len2 = bpf_probe_read_kernel_str(&temp[index & (PATH_MAX - 1)], PATH_MAX, &temp[PATH_MAX]);
        index += copied_len2 - 1; // -1 since this an index, not a length
        if (index >= PATH_MAX - 1) {
            break;
        }
    }
    
    // Copy the path to the final destination
    // index + 1 is used here because index is used as a length not an index here
    return bpf_probe_read_kernel_str(dest, ((index + 1) & (PATH_MAX - 1)), &temp[0]);
}

/**
 * breakaway_map_callback() - Callback function to check if the current process needs to breakaway.
 *
 * This function is called for each entry in the breakaway processes map.
 * It checks if the current process matches the breakaway process criteria.
 */
static long breakaway_map_callback(struct bpf_map *map, const uint32_t *key, breakaway_process *value, exec_event_metadata **ctx) {
    exec_event_metadata *event = *ctx;

    if (value->tool[0] == '\0') {
        // Reached the end of the map, the rest of the elements are not populated
        return 1;
    }

    char *toolname = &event->exe_path[event->exe_name_start_index & (PATH_MAX - 1)];

    bool exe_match = string_contains(toolname, event->exe_name_len, value->tool, value->tool_len, /* case_sensitive */ true);
    // Args can be ignored if they weren't specified in the breakaway process map
     bool args_match = value->arguments[0] == '\0' 
         ? true 
         : string_contains(value->arguments, value->arguments_len, event->args, event->args_len, /* case_sensitive */ !value->args_ignore_case);

    event->needs_breakaway = exe_match && args_match;

    // If we already found a match, we can return 1 here to terminate the loop early
    return event->needs_breakaway ? 1 : 0;
}

/**
 * basename_loop_callback() - Loop callback to find the basename of the executable path.
 *
 * The basename is the last component of the path, which is the executable name.
 * Finds the starting index of the basename in the path.
 */
static long basename_loop_callback(u64 index, exec_event_metadata **ctx) {
    exec_event_metadata *event = *ctx;
    u64 i = (event->exe_path_len - index) & (PATH_MAX - 1);

    // Since bpf_loop can only start at 0 and increment, keep track of the last '/'
    // If the next character is a '\0' then it's a trailing '/' which can be ignored.
    if (event->exe_path[i] == '/' && event->exe_path[i + 1 & (PATH_MAX - 1)] != '\0') {
        event->exe_name_start_index = i + 1;
        return 1;
    }

    return 0;
}

/**
 * process_needs_breakaway() - Verifies if the given process needs to breakaway by updating
 * the given event `needs_breakaway` field. Returns non-zero if the breakaway map could not be retrieved
 * 
 * This function uses bpf_loop instead of a for/while loop because using an escape
 * hatch allows us to reduce the amount of time needed to verify the program.
 * Additionally, bpf_loop lets us have much bigger loops without hitting the instruction limit.
 */
static int process_needs_breakaway(exec_event_metadata *event, pid_t runner_pid) {
    event->needs_breakaway = false;

    // The path that we have is the full path to the executable
    // Breakaway processes match with the executable path atom so we need to find the basename
    bpf_loop((event->exe_path_len & (PATH_MAX - 1)), basename_loop_callback, &event, 0);

    // -1 to Ignore the null terminating character
    event->exe_name_len = event->exe_path_len - event->exe_name_start_index - 1;

    // Retrieve the corresponding breakaway map given the runner id
    void *breakaway_processes = bpf_map_lookup_elem(&breakaway_processes_per_pip, &runner_pid);
    if (breakaway_processes == NULL) {
        return -1;
    } 

    // Check if the process needs to breakaway
    bpf_for_each_map_elem(breakaway_processes, breakaway_map_callback, &event, 0);
    
    return 0;
}

#endif // __PUBLIC_SRC_SANDBOX_LINUX_EBPF_EBPFUTILITIES_H