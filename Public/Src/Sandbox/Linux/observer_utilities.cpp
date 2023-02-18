// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#include "observer_utilities.hpp"
#include <stdlib.h>
#include <string.h>
#include <limits.h>

bool resolve_filename_with_env(const char *filename, mode_t &mode, std::string &path)
{
    mode = 0;

    if (*filename == '\0' || strchr(filename, '/') != NULL)
    {
        // Filename should not contain a '/' at any point because that would indicate that its an absolute/relative path
        // This path doesn't need to be resolved and we can return true here because one of the following cases occurs:
        // 1. Already an absolute path
        // 2. A relative path (therefore it would be resolved against the working directory, not PATH)
        // 3. An empty string, in this case we want the exec call to continue, fail and set errno to indicate what went wrong to the caller.
        path = filename;
        return true;
    }

    char *env_path = getenv("PATH");
    if (!env_path)
    {
        env_path = "/usr/bin";
    }

    std::string env_path_str(env_path);
    size_t pos = 0;
    size_t start = 0;
    std::string root_path;

    while (true)
    {
        pos = env_path_str.find(':', start);

        if (pos == std::string::npos)
        {
            break;
        }

        root_path = env_path_str.substr(start, pos-start);
        start = pos + 1; /*+1 to account for the delimiter ':'*/

        if (check_if_path_exists(root_path, filename, path, mode))
        {
            return true;
        }
    }

    root_path = env_path_str.substr(start);
    return check_if_path_exists(root_path, filename, path, mode);
}

bool check_if_path_exists(std::string root, std::string filename, std::string &path, mode_t &mode)
{
    std::string finalPath = root + "/" + filename;
    struct stat buf;

    // Call the interposed stat instead of the real one here so we can report it back to the managed layer
#if (__GLIBC__ == 2 && __GLIBC_MINOR__ < 33)
    mode = __lxstat(1, finalPath.c_str(), &buf) == 0
#else
    mode = lstat(finalPath.c_str(), &buf) == 0
#endif
        ? buf.st_mode
        : 0;

    if (mode != 0)
    {
        path = finalPath;
        return true;
    }

    return false;
}

ptrdiff_t get_variadic_argc(va_list args)
{
    ptrdiff_t argc;

    // va_arg() will read the current argument pointer
    // Each time it's called, the argument pointer is moved up by one and the value of that argument is returned
    for (argc = 1; va_arg(args, const char *); argc++)
    {
        if (argc == INT_MAX)
        {
            va_end(args);
            errno = E2BIG;
            return -1;
        }
    }

    return argc;
}

void parse_variadic_args(const char *arg, ptrdiff_t argc, va_list args, char **argv)
{
    argv[0] = (char *)arg;
    for (ptrdiff_t i = 1; i <= argc; i++)
    {
        // va_arg will return a pointer to the current argument that we will store in argument vector
        argv[i] = va_arg(args, char *);
    }
}
