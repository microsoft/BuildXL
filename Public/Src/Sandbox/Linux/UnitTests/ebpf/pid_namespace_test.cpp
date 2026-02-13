// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <sched.h>
#include <sys/wait.h>
#include <sys/stat.h>
#include <pthread.h>
#include <string.h>
#include <errno.h>

/**
 * Test binary that exercises PID namespace translation in the eBPF sandbox.
 *
 * This program creates a new PID namespace, forks a child into it, and the child
 * spawns multiple threads that each perform file operations (stat on absent files).
 * The eBPF sandbox must correctly translate the PIDs of all threads back to the
 * runner's namespace.
 *
 * Each thread stats a unique absent file (<base_path>_thread_<N>) so that accesses
 * are not deduped by the native side event cache. The main thread also stats its own
 * unique file (<base_path>_main). The C# test side can then count the number of
 * distinct accesses and compare against the expected thread count + 1. It also 
 * prints to stdou the pids observed by the child to verify that it is running in 
 * a new PID namespace (should see PID 1).
 *
 * Expected arguments: <base_path> <num_threads>
 *   base_path:    a base path used to construct per-thread absent file paths
 *   num_threads:  number of additional threads to spawn in the child process
 */

#define MAX_THREADS 16

#define MAX_PATH_LEN 512

struct thread_args {
    char path[MAX_PATH_LEN];
    int thread_id;
};

void *thread_func(void *arg) {
    struct thread_args *targs = (struct thread_args *)arg;
    struct stat st;

    // Each thread stats its own unique absent file. The eBPF sandbox will observe this
    // and must translate the PID consistently regardless of which thread triggers the probe.
    // Using distinct files avoids native-side deduplication so each access is reported.
    int ret = stat(targs->path, &st);
    if (ret != 0 && errno != ENOENT) {
        fprintf(stderr, "Thread %d: stat(%s) failed unexpectedly: %s\n", targs->thread_id, targs->path, strerror(errno));
    }

    return NULL;
}

int child_main(const char *base_path, int num_threads) {
    fprintf(stdout, "Child process started with PID %d (should be 1 in new namespace)\n", getpid());

    pthread_t threads[MAX_THREADS];
    struct thread_args targs[MAX_THREADS];

    // Cap threads at MAX_THREADS
    if (num_threads > MAX_THREADS) {
        num_threads = MAX_THREADS;
    }

    // Spawn threads that each stat a unique absent file
    for (int i = 0; i < num_threads; i++) {
        snprintf(targs[i].path, MAX_PATH_LEN, "%s_thread_%d", base_path, i);
        targs[i].thread_id = i;
        if (pthread_create(&threads[i], NULL, thread_func, &targs[i]) != 0) {
            fprintf(stderr, "Failed to create thread %d: %s\n", i, strerror(errno));
            return 1;
        }
    }

    // Also do a stat from the main thread on its own unique absent file
    struct stat st;
    char main_path[MAX_PATH_LEN];
    snprintf(main_path, MAX_PATH_LEN, "%s_main", base_path);
    stat(main_path, &st);

    // Wait for all threads to complete
    for (int i = 0; i < num_threads; i++) {
        pthread_join(threads[i], NULL);
    }

    fprintf(stdout, "All %d threads completed successfully\n", num_threads);
    return 0;
}

int main(int argc, char **argv) {
    if (argc < 3) {
        fprintf(stderr, "Usage: %s <path_to_stat> <num_threads>\n", argv[0]);
        return 1;
    }

    const char *path = argv[1];
    int num_threads = atoi(argv[2]);

    if (num_threads <= 0) {
        fprintf(stderr, "num_threads must be positive\n");
        return 1;
    }

    fprintf(stdout, "Parent PID: %d, creating new PID namespace\n", getpid());

    // Create a new PID namespace. The next fork will place the child as PID 1 in it.
    if (unshare(CLONE_NEWPID) != 0) {
        fprintf(stderr, "unshare(CLONE_NEWPID) failed: %s\n", strerror(errno));
        return 1;
    }

    pid_t pid = fork();
    if (pid < 0) {
        fprintf(stderr, "fork() failed: %s\n", strerror(errno));
        return 1;
    }

    if (pid == 0) {
        // Child: runs in the new PID namespace
        return child_main(path, num_threads);
    }

    // Parent: wait for the child
    int status;
    if (waitpid(pid, &status, 0) < 0) {
        fprintf(stderr, "waitpid failed: %s\n", strerror(errno));
        return 1;
    }

    if (WIFEXITED(status)) {
        int exit_code = WEXITSTATUS(status);
        fprintf(stdout, "Child exited with code %d\n", exit_code);
        return exit_code;
    }

    fprintf(stderr, "Child did not exit normally\n");
    return 1;
}
