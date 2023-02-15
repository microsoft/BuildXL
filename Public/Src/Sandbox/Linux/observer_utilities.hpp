// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma once

#include <sys/stat.h>
#include <string>
#include <stdarg.h>
#include <cstddef>

// Resolves a provided filename against the environment by checking if it exists by using stat
// This closely follows the logic used by glibc: https://codebrowser.dev/glibc/glibc/posix/execvpe.c.html
bool resolve_filename_with_env(const char *filename, mode_t &mode, std::string &path);

// Appends filename to root, checks if it exists by calling stat and then sets path if it does exist
bool check_if_path_exists(std::string root, std::string filename, std::string &path, mode_t &mode);

// An example on how to parse variadic args where this code was derived from can be found here: https://github.com/bminor/glibc/blob/master/posix/execl.c
// Parses the arguement count of a variable set of arguments passed to a function
// The final argument should have a value of nullptr
ptrdiff_t get_variadic_argc(va_list args);

// Given a va_list and an argument count, parse arguments into argv
void parse_variadic_args(const char *arg, ptrdiff_t argc, va_list args, char **argv);