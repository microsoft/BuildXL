// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

bool is_null_or_empty(char const *input);

/**
 * This is a function to ensure env (indicated by envPrefix which contains the name of env and "=", e.g. LD_PRELOAD=) is in envp[] 
 * and paths (given in the arg list terminated with NULL) are in env.
 * It returns a pointer to char * [] (following the same format of "envp") which has env containing paths.
 * Case 1. envp contains env and env contains all paths, return envp
 * Case 2. envp contains env but env doesn't contain all paths. Then we 
 *        1) create newenvp, 
 *        2) copy all kvps in envp to newenvp,
 *        3) replace env in newenvp with a kvp containing all paths,
 *        4) return newenvp
 * Case 3. envp doesn't contain env. Then we 
 *        1) create newenvp, 
 *        2) copy all kvps in envp to newenvp, 
 *        3) add env containing all paths to newenvp,
 *        4) return newenvp
*/
char** ensure_paths_included_in_env(char *const envp[], char const *envPrefix, const char *arg, ...);

/**
 * This is a function to ensure "envName=envValue" is in envp[]
 * It returns a pointer to char * [] (following the same format of "envp") which "envName=envValue".
*/
char** ensure_env_value(char *const envp[], char const *envName, const char *envValue);