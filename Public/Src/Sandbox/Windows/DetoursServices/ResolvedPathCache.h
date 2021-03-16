// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#include <map>
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

typedef std::pair<std::vector<std::wstring>, std::map<std::wstring, ResolvedPathType>> ResolvedPathCacheEntries;

class ResolvedPathCache {
public:
    inline bool InsertResolvingCheckResult(const std::wstring& path, bool result)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock);

        const std::wstring normalizedPath = Normalize(path);
        if (!m_pathTree.TryInsert(normalizedPath))
        {
            return false;
        }

        return m_resolverCache.emplace(normalizedPath, result).second;
    }

    inline const bool* GetResolvingCheckResult(const std::wstring& path)
    {
        return Find(m_resolverCache, Normalize(path));
    }

    inline bool InsertResolvedPathWithType(const std::wstring& path, std::wstring& resolved, DWORD type)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock);
        const std::wstring normalizedPath = Normalize(path);
        if (!m_pathTree.TryInsert(Normalize(normalizedPath)))
        {
            return false;
        }

        return m_targetCache.emplace(normalizedPath, std::make_pair(resolved, type)).second;
    }

    inline const std::pair<std::wstring, DWORD>* GetResolvedPathAndType(const std::wstring& path)
    {
        return Find(m_targetCache, Normalize(path));
    }

    inline bool InsertResolvedPaths(const std::wstring& path, bool preserveLastReparsePointInPath, std::vector<std::wstring>&& insertion_order, std::map<std::wstring, ResolvedPathType>&& resolved_paths)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock);

        const std::wstring normalizedPath = Normalize(path);

        if (!m_pathTree.TryInsert(Normalize(normalizedPath)))
        {
            return false;
        }

        for (auto iter = resolved_paths.begin(); iter != resolved_paths.end(); ++iter) {
            if (!m_pathTree.TryInsert(Normalize(iter->first)))
            {
                return false;
            }
        }

        return m_paths.emplace(std::make_pair(normalizedPath, preserveLastReparsePointInPath), std::make_pair(insertion_order, resolved_paths)).second;
    }

    inline const ResolvedPathCacheEntries* GetResolvedPaths(const std::wstring& path, bool preserveLastReparsePointInPath)
    {
        return Find(m_paths, std::make_pair(Normalize(path), preserveLastReparsePointInPath));
    }

    void Invalidate(const std::wstring& path)
    {
        ResolvedPathCacheWriteLock w_lock(m_lock);
        
        const std::wstring normalizedPath = Normalize(path);

        InvalidateThisPath(Normalize(normalizedPath));

        // Invalidate all its descendants
        std::vector<std::wstring> descendants;
        m_pathTree.RetrieveAndRemoveAllDescendants(normalizedPath, descendants);

        for (auto iter = descendants.begin(); iter != descendants.end(); ++iter)
        {
            InvalidateThisPath(*iter);
        }
    }
    
    void InvalidateThisPath(const std::wstring & path)
    {
        m_resolverCache.erase(path);
        m_targetCache.erase(path);

        // Let's make sure we invalidate the cache for both preserveLastReparsePoint options
        m_paths.erase(std::make_pair(path, true));
        m_paths.erase(std::make_pair(path, false));

        for (auto it = m_paths.begin(), it_next = it; it != m_paths.end(); it = it_next)
        {
            ++it_next;
            auto mappings = it->second.second;
            if (mappings.find(path) != mappings.end())
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
    template<typename K, typename V>
    const V* Find(const std::map<K, V>& map, const K& path)
    {
        ResolvedPathCacheReadLock r_lock(m_lock);

        auto iter = map.find(path);
        if (iter != map.end())
        {
            return &iter->second;
        }

        return nullptr;
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
    std::map<std::wstring, bool> m_resolverCache;

    // A mapping used to cache DeviceControl calls when querying targets of reparse points, used to avoid unnecessary I/O
    std::map<std::wstring, std::pair<std::wstring, DWORD>> m_targetCache;

    // A mapping used to cache all intermediate paths and the final fully resolved path (value) of an unresolved base 
    // path where its last segment has to be resolved or not(key)
    std::map<std::pair<std::wstring, bool>, ResolvedPathCacheEntries> m_paths;

    // All the paths the cache is aware of
    PathTree m_pathTree;
};
