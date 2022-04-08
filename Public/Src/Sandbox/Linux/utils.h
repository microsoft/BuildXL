// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#ifdef __cplusplus
#define DLL_EXPORT extern "C"
#else
#define DLL_EXPORT
#endif

DLL_EXPORT bool is_null_or_empty(char const *input);

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
DLL_EXPORT char** ensure_paths_included_in_env(const char *const envp[], char const *envPrefix, const char *arg, ...);

/**
 * This is a function to ensure "envName=envValue" is in envp[]
 * It returns a pointer to char * [] (following the same format of "envp") which "envName=envValue".
*/
DLL_EXPORT char** ensure_env_value(const char *const envp[], char const *envName, const char *envValue);

/**
 * Tries to match 'prefix' from the beggining of 'src'.
 * Upon success, returns a pointer to the next character (right after 
 * the matched prefix) in 'src' is returned; otherwise returns NULL.
 */
DLL_EXPORT const char* skip_prefix(const char *src, const char *prefix);

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
DLL_EXPORT const char* add_value_to_env(const char *src, const char *value_to_add, const char *envPrefix);

/**
 * Scrubs the 'value_to_scrub' values from 'src' if 'src' begins with "LD_PRELOAD=";
 * otherwise returns the original value provided in 'src'.
 * 
 * Only whole values are scrubbed.  That is, the part after "LD_PRELOAD=" in 'src'
 * is treated as a colon-separated list of values; out of those values, those that
 * are equal to 'value_to_scrub' are removed.
 * 
 * 'buf' is an auxiliary buffer that must be at least as big as 'src'.
 * 
 * The result is either stored in 'buf' or 'src' is returned; when the latter is 
 * the case, the value written in 'buf' is unspecified.  In either case, the return
 * value is a pointer to where the result is (either 'src' or 'buf').
 */
DLL_EXPORT const char* scrub_ld_preload(const char *src, const char *value_to_scrub, char *buf);

/**
 * If 'envp' does not contain a variable named "LD_PRELOAD" or the value of that
 * environment variable does not include 'path', 'envp' is returned.
 * 
 * Otherwise, a new array of 'char*' pointers is allocated.  The values from 'envp'
 * are copied into it verbatim except that 'path' is excluded from the "LD_PRELOAD" value.
 * 
 * Whenever the returned pointer is different from 'envp', the caller is responsible for freeing it.
 */
DLL_EXPORT char** remove_path_from_LDPRELOAD(const char *const envp[], const char *path);

// Test wrappers to make p-invoke easier.

DLL_EXPORT const bool add_value_to_env_for_test(const char *src, const char *value_to_add, const char *envPrefix, char *buf);
DLL_EXPORT const bool ensure_env_value_for_test(const char *const envp[], char const *envName, const char *envValue, char *buf);
DLL_EXPORT const bool ensure_2_paths_included_in_env_for_test(const char *const envp[], char const *envPrefix, const char *path0, const char *path1, char *buf);
DLL_EXPORT const bool ensure_1_path_included_in_env_for_test(const char *const envp[], char const *envPrefix, const char *path, char *buf);
DLL_EXPORT const void scrub_ld_preload_for_test(const char *src, const char *value_to_scrub, char *buf);
DLL_EXPORT const bool remove_path_from_LDPRELOAD_for_test(const char *const envp[], char *path, char *buf0, char *buf1, char *buf2);