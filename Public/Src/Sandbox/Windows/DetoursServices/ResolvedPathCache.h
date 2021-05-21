// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#pragma once

#include <map>
#include <memory>
#include <shared_mutex>
#include <vector>

#include "PathTree.h"

typedef std::shared_mutex ResolvedPathCacheLock;
typedef std::unique_lock<ResolvedPathCacheLock> ResolvedPathCacheWriteLock;
typedef std::shared_lock<ResolvedPathCacheLock> ResolvedPathCacheReadLock;

enum class ResolvedPathType 
{
    Intermediate, // Identifies a path that was found as an intermediate result when resolving all reparse point occurences of a specific base path
    FullyResolved // Identifies the fully resolved path that does not contain any reparse point parts anymore
};

template<typename K> struct Possible
{
    bool Found;
    K Value;
};

// Shared pointers are used because keeping the actual objects in the map either results in copying, or getting pointers to the map memory which can become invalid when the map is changed.
// Raw pointers are not used because the creation of the object is in a different location than the removal/destruction of the object, and it is hard to know when the last reference will be gone.
typedef std::pair<std::shared_ptr<std::vector<std::wstring>>, std::shared_ptr<std::map<std::wstring, ResolvedPathType, CaseInsensitiveStringLessThan>>> ResolvedPathCacheEntries;

static CaseInsensitiveStringLessThan caseInsensitiveLessThan = CaseInsensitiveStringLessThan();

// Case insensitive comparer for the target cache to handle pairs (wstring, bool). Delegates the wstrings to
// the CaseInsensitiveStringLessThan class.
struct CaseInsensitiveTargetCacheLessThan : public std::binary_function<std::pair<std::wstring, bool>, std::pair<std::wstring, bool>, bool> {
    bool operator()(const std::pair<std::wstring, bool>& lhs, const std::pair<std::wstring, bool>& rhs) const {
        if (lhs.second != rhs.second)
        { 
            return lhs.second;
        }
        else
        {
            return caseInsensitiveLessThan.operator()(lhs.first, rhs.first);
        }
    }
};

// A note on how paths are stored in the cache: Paths coming from detoured functions may vary in casing and may or may not
// have a trailing slash. Standard path canonicalization done as part of setting up the detours policy does not take care of these 
// differences, but the resolved path cache should treat those as equivalent paths (e.g C:\foo, C:\FOO and C:\foo\ should be considered equivalent directories).
// All cache related structures use a case insensitive comparer for paths. Observe this doesn't change any user-facing paths (i.e. 
// paths reported or used for real accesses)
class ResolvedPathCache {
public:
    inline bool InsertResolvingCheckResult(const std::wstring& path, bool result)
    {
        // Using the cache is best effort, as this is faster than waiting on locks.  It is not incorrect to not have a value in the cache, just might result in more I/O when the file is not found in the cache.
        ResolvedPathCacheWriteLock w_lock(m_lock, std::try_to_lock);
        if (!w_lock.owns_lock())
        {
          return false;
        }

        const std::wstring normalizedPath = Normalize(path);
        if (!m_pathTree.TryInsert(normalizedPath))
        {
            return false;
        }
        
        return m_resolverCache.emplace(normalizedPath, result).second;
    }

    inline const Possible<bool> GetResolvingCheckResult(const std::wstring& path)
    {
        return Find(m_resolverCache, Normalize(path));
    }

    inline bool InsertResolvedPathWithType(const std::wstring& path, std::wstring& resolved, DWORD type)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock, std::try_to_lock);
        if (!w_lock.owns_lock())
        {
          return false;
        }

        const std::wstring normalizedPath = Normalize(path);
        if (!m_pathTree.TryInsert(normalizedPath))
        {
            return false;
        }

        return m_targetCache.emplace(normalizedPath, std::make_pair(resolved, type)).second;
    }

    inline const Possible<std::pair<std::wstring, DWORD>> GetResolvedPathAndType(const std::wstring& path)
    {
        return Find(m_targetCache, Normalize(path));
    }

    inline bool InsertResolvedPaths(
        const std::wstring& path,
        bool preserveLastReparsePointInPath,
        std::shared_ptr<std::vector<std::wstring>>& insertion_order,
        std::shared_ptr<std::map<std::wstring, ResolvedPathType, CaseInsensitiveStringLessThan>>& resolved_paths)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock, std::try_to_lock);
        if (!w_lock.owns_lock())
        {
          return false;
        }

        const std::wstring normalizedPath = Normalize(path);

        if (!m_pathTree.TryInsert(normalizedPath))
        {
            return false;
        }

        for (auto iter = resolved_paths->begin(); iter != resolved_paths->end(); ++iter)
        {
            if (!m_pathTree.TryInsert(Normalize(iter->first)))
            {
                return false;
            }
        }

        return m_paths.emplace(std::make_pair(normalizedPath, preserveLastReparsePointInPath), std::make_pair(insertion_order, resolved_paths)).second;
    }

    inline const Possible<ResolvedPathCacheEntries> GetResolvedPaths(const std::wstring& path, bool preserveLastReparsePointInPath)
    {
        return Find(m_paths, std::make_pair(Normalize(path), preserveLastReparsePointInPath));
    }

    void Invalidate(const std::wstring& path, bool isDirectory)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock);

        const std::wstring normalizedPath = Normalize(path);

        // Invalidating the back references to this normalized path is important only because by deleting or creating this link other links type (intermediate/fully resolved) may be out of date.
        InvalidateThisPath(normalizedPath);

        if (isDirectory)
        {

            // Invalidate all its descendants
            // This is for absent path probes, if something probes a\b\c and suddently a\b changes, a\b\c might point somewhere different.  The same is not true for file symlinks
            std::vector<std::wstring> descendants;
            m_pathTree.RetrieveAndRemoveAllDescendants(normalizedPath, descendants);
            for (auto iter = descendants.begin(); iter != descendants.end(); ++iter)
            {
                InvalidateThisPath(*iter);
            }
        }
    }
    
    void InvalidateThisPath(const std::wstring & path)
    {
        m_resolverCache.erase(path);
        m_targetCache.erase(path);

        // Let's make sure we invalidate the cache for both preserveLastReparsePoint options
        m_paths.erase(std::make_pair(path, true));
        m_paths.erase(std::make_pair(path, false));

        // This invalidation is rather expensive as it traverses the whole cache to remove entries.
        for (auto it = m_paths.begin(), it_next = it; it != m_paths.end(); it = it_next)
        {
            ++it_next;
            auto mappings = it->second.second;
            if (mappings->find(path) != mappings->end())
            {
                m_paths.erase(it);
            }
        }
    }

    ResolvedPathCache() = default;
    ~ResolvedPathCache() = default;
    ResolvedPathCache(const ResolvedPathCache&) = delete;
    ResolvedPathCache& operator=(const ResolvedPathCache&) = delete;

    static ResolvedPathCache& Instance()
    {
        static ResolvedPathCache instance;
        return instance;
    }

private:
    // Find should not return a pointer, as that memory can become invalid if a different thread adds/removes from the map.
    // Instead, the value store in the map should be a pointer so that the memory isn't copied.
    template<typename K, typename V, typename C>
    const Possible<V> Find(std::map<K, V, C>& map, const K& path)
    {
        ResolvedPathCacheReadLock r_lock(m_lock);
        auto iter = map.find(path);
        Possible<V> p;
        p.Found = iter != map.end();
        if (p.Found)
        {
            p.Value = iter->second;
        }

        return p;
    }

    // CanonicalPath does not canonicalize trailing slashes for directories
    // But the cache structures need exact string matching, so we do it here
    inline std::wstring Normalize(const std::wstring& path)
    {
        if (path.size() > 0 && IsDirectorySeparator(path.back()))
        {
            std::wstring normal = path.substr(0, path.size() - 1);
            return normal;
        }

        return path;
    }

    ResolvedPathCacheLock m_lock;

    // A mapping used to cache if base paths need to be resolved (no entry) or have previously been fully resolved
    std::map<std::wstring, bool, CaseInsensitiveStringLessThan> m_resolverCache;

    // A mapping used to cache DeviceControl calls when querying targets of reparse points, used to avoid unnecessary I/O
    std::map<std::wstring, std::pair<std::wstring, DWORD>, CaseInsensitiveStringLessThan> m_targetCache;

    // A mapping used to cache all intermediate paths and the final fully resolved path (value) of an unresolved base 
    // path where its last segment has to be resolved or not(key)
    std::map<std::pair<std::wstring, bool>, ResolvedPathCacheEntries, CaseInsensitiveTargetCacheLessThan> m_paths;

    // All the paths the cache is aware of.
    //
    // This path tree is used for cache invalidation. Suppose that a process accesses D1 and D1\E1 where both D1 and E1 are
    // symlinks. The cache will have entries for both D1 and D1\E1. If D1 is removed (e.g., by calling RemoveDirectory), then
    // the entry for D1\E1 in the cache needs to be removed as well. Otherwise, if subsequently the process decides to create
    // D1\E1 again but D1 points to a different target, then any access of D1\E1 will get the wrong entry from the cache.
    PathTree m_pathTree;
};
