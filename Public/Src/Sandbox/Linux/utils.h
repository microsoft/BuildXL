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

/**
 * Tries to match 'prefix' from the beggining of 'src'.
 * Upon success, returns a pointer to the next character (right after 
 * the matched prefix) in 'src' is returned; otherwise returns NULL.
 */
const char* skip_prefix(const char *src, const char *prefix);

/**
 * envPrefix is in the format of the env name and "=", e.g. LD_PRELOAD=
 * Append the 'value_to_add' to 'src' if 
 * 1) 'src' begins with envPrefix;
 * 2)  the 'value_to_add' does not exist in colon-separated values of envPrefix<values>;
 * Otherwise returns the original value provided in 'src'.
 * 
 * The part after envPrefix in 'src' is treated as a colon-separated list of values;
 * If 'value_to_add' doesn't exist in the list, it will be appended at the end of 'src' following a colon if needed.
 * 
 * 'buf' is an auxiliary buffer that must be at least as big as 'src' + '：' + 'value_to_add'.
 * 
 * The result is either stored in 'buf' or 'src' is returned; when the latter is 
 * the case, the value written in 'buf' is unspecified.  In either case, the return
 * value is a pointer to where the result is (either 'src' or 'buf').
 */
const char* add_value_to_env(const char *src, const char *value_to_add, const char *envPrefix);

/**
 * Test wrappers to make p-invoke easier.
 */
extern "C" {
    const bool add_value_to_env_for_test(const char *src, const char *value_to_add, const char *envPrefix, char *buf);
    const bool ensure_env_value_for_test(char *const envp[], char const *envName, const char *envValue, char *buf);
    const bool ensure_2_paths_included_in_env_for_test(char *const envp[], char const *envPrefix, const char *path0, const char *path1, char *buf);
    const bool ensure_1_path_included_in_env_for_test(char *const envp[], char const *envPrefix, const char *path, char *buf);
}