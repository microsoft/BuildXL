# Build Accelerator Cache

## Introduction
The caching functionality in Build Accelerator (BuildXL) is provided by a set of components which is packaged separately and can be used independently of the BuildXL Build Engine.  This functionality goes beyond the typical key-value construct, having features designed to support build engines that can handle non-deterministic and non-hermetic tasks.

## Functional Architecture
The Build Accelerator Cache [BuildXL Cache, for short], is logically composed of two parts: a Content Store and a Memoization Store.

### Content Store 
The Content Store is effectively a CAS (Content Addressable Store).  There are methods to store and retrieve content **blobs**.  In this document we use the term "blob" to differentiate cache content from "files on disk" - the term is not used in code much, though.  Its functionality is embodied by the [`IContentStore`](ContentStore\Interfaces\Stores\IContentStore.cs) interface, with [`IContentSession`](ContentStore\Interfaces\Sessions\IContentSession.cs) providing methods to store and retrieve content from the CAS.  

Each **blob** in the cache is identified by a hash of its contents.  A few different **hashing algorithms** can be used, as seen in the [Hashing](\ContentStore\Hashing) directory.  At this time such algorithms are not simple MD5 or SHA-* hashes of the content stream, but rather a hash-of-hashes.  A blob is divided into segments, each segment hashed independently, and a hash-of-hashes computed as the blob's identifier.  There are variants for fixed- and variable-length segmentation, each optimized for use with different cloud storage APIs.  These APIs use the segmented nature of this scheme to validate and de-duplicate content in transmission and at rest. See the [paged hash spec](../../../Documentation/Specs/PagedHash.md) for more detailed information.

Content Store implementations are responsible for managing storage utilization - some do it based on space quotas, some on time-based retention policies.  

In some contexts BuildXL needs to signal to the cache that it _intends_ to use a given blob, without retrieving it from the cache.  So, besides the "add" and "get" methods typical of CAS implementations, a Content Store also provides a `Pin` method, which which will: 
1. Check that pinned blob is available and could be retrieved if needed later during a client's session;
1. Ensure that the blob is not deleted by garbage collection or space management activities for the duration of the client's session. 

### Memoization Store 

The **Memoization Store** is used to look up a set of blobs, based on an arbitrary **lookup fingerprint**.  This implementation is embodied by the [`IMemoizationStore`](MemoizationStore\Interfaces\Sessions) interface, with a [`IMemoizationSession`](MemoizationStore\Interfaces\Sessions\IMemoizationSession.cs) providing methods to insert and look up a content-set based on its fingerprint.  Fingerprints are domain-dependent, and handled opaquely by the Memoization Store.  

#### Two-Phase Cache Lookup
The Memoization Store APIs were designed to address BuildXL's unique cache lookup requirements.  To understand how, one needs to first review what BuildXL has to work with, in order to determine whether to reuse cached outputs for previously executed build tasks:   

- Before executing a build, BuildXL parses build specs, which include: (a) the tasks involved in the build; (b) dependencies amongst such tasks; and (c) the inputs used by each task. Such inputs are inferrable from build specs and are called **static inputs**. 

- Unfortunately the list of inputs required by a task may be incomplete.  For example, a C++ compilation task may declare the exact set of input _*.CPP/*.CXX_ source files required, while not declaring the complete set of _*.HPP/*.HXX_ header files.  Determining the set of includes would typically require running the C++ pre-processor, which is undesirable.  Of course, when BuildXL _does_ execute the C++ compilation task, it can observe the _actual_ header files accessed by the compiler.  Those accesses are recorded as **dynamic inputs**.

To accommodate these static and dynamic inputs, the Memoization Store's data model goes beyond the typical key->value model.  

Consider the following table, which illustrates the Memoization Store's conceptual data model: 

| WeakFingerprint | CasElement | HashElement | Content List |
|:----------------:|:-----------------------------:|:------------------------------------------:|:--------------------------------------|
| &nbsp; \|-------------- | **StrongFingerprint** | --------------\| &nbsp;
| _F<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>H<sub>1</sub>_ | {_C<sub>1</sub>, C<sub>2</sub>, ..._ }|
| _F<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>H<sub>2</sub>_ | {_C<sub>3</sub>, C<sub>4</sub>, ..._ }|
| _F<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>_ | _F<sub>1</sub>C<sub>1</sub>H<sub>3</sub>_ | {_C<sub>5</sub>, C<sub>6</sub>, ..._ }|
| _F<sub>1</sub>_ | _F<sub>1</sub>C<sub>2</sub>_ | _F<sub>1</sub>C<sub>2</sub>H<sub>1</sub>_ | {_C<sub>7</sub>, C<sub>8</sub>, C<sub>9</sub>, ..._ }|
| _F<sub>2</sub>_ | _F<sub>2</sub>C<sub>1</sub>_ | _F<sub>2</sub>C<sub>1</sub>H<sub>1</sub>_ | {_C<sub>10</sub>, ..._ }              |
| ...              |                               |                                            |                                       |

The `WeakFingerprint`, the `CasElement` and the `HashElement` are all used in the lookup process.  The **Content List** is the result of a lookup, an ordered list of content identifiers which can be used to retrieve blobs from the CAS.  

1. To look up a given build task, BuildXL computes a `WeakFingerprint`, typically a hash-of-hashes of the files declared as the task's **static inputs**. Let's say the weak fingerprint is _F<sub>1</sub>_, as seen in the table above.  A call to 
**GetSelectors( _F<sub>1</sub>_ )** 
would return a set of `Selector` objects associated with that weak fingerprint.  Each `Selector` is tuple of the form <_CasElement_, _HashElement_>.  Based on the example the result would be  
{ <_F<sub>1</sub>C<sub>1</sub>_, _F<sub>1</sub>C<sub>1</sub>H<sub>1</sub>_>,
  <_F<sub>1</sub>C<sub>1</sub>_, _F<sub>1</sub>C<sub>1</sub>H<sub>2</sub>_>, 
  <_F<sub>1</sub>C<sub>1</sub>_, _F<sub>1</sub>C<sub>1</sub>H<sub>3</sub>_>,
  <_F<sub>1</sub>C<sub>2</sub>_, _F<sub>1</sub>C<sub>2</sub>H<sub>1</sub>_> }. 
The `CasElement` term is a content identifier for a CAS blob. The the contents of that blob being application dependent.  
In the BuildXL case, the build engine uses the blob referenced by `CasElement` to store the list of **dynamic inputs** observed doing a previous execution of the task.  In the C++ example above, this would be paths to header files last used by the compiler.  
The `HashElement` is an applicationdefined opaque byte-array, used to test "how good of a match" this cache record is to the application's need.

1. BuildXL evaluates the returned `Selectors` attempting to find one that matches the file system state for the present build.  For each `Selector`, BuildXL retrieves the blob referenced by its `CasElement` and, using the paths listed as **dynamic inputs**, computes a hash-of-hashes which summarizes the file system state for the relevant files.  The hash-of-hashes is then compared to the `HashElement` in the `Selector` (and possibly to any other `Selectors` with the same `CasElement`).  In the example above, let's say that it matches the value 
_F<sub>1</sub>C<sub>1</sub>H<sub>2</sub>_.  
To retrieve the associated content, BuildXL calls 
**GetContentHashList(
   _F<sub>1</sub>_,_F<sub>1</sub>C<sub>1</sub>_, 
   _F<sub>1</sub>C<sub>1</sub>H<sub>2</sub>_)**.
Based on the table, the value returned would be 
   { 
     _C<sub>3</sub>, 
     C<sub>4</sub>, ..._ 
   }. 

1. In another scenario let's say that BuildXL calls `GetSelectors` as described above, but receives a result that amounts to _"not found"_, or that `GetSelectors` returns some tuples, but BuildXL is unable to find one with a matching `HashElement`.  Both cases are cache misses. As BuildXL executes the task, it observes its dynamic inputs and collects its outputs. It stores the collected output into the CAS and uses the dynamic inputs observations to compute the `HashElement` value _F<sub>1</sub>C<sub>1</sub>H<sub>new</sub>_ .  With that information it calls 
**AddOrGetContentHashList( 
   _F<sub>1</sub>_,_F<sub>1</sub>C<sub>1</sub>_, 
   _F<sub>1</sub>C<sub>1</sub>H<sub>2</sub>_,
   _F<sub>1</sub>C<sub>1</sub>H<sub>new</sub>_ , 
   { _C<sub>new1</sub>, C<sub>new2</sub>_ } )**,
which effectively adds the new entry to the cache.  

Notes:
  - In reality, `GetContentHashList` is called with a `StrongFingerprint` object, effectively a tuple <_WeakFingerprint, CasElement, HashElement_>).  Similarly, `AddOrGetContentHashList` also takes a `StrongFingerprint`, plus a few other arguments omitted here for brevity.

  - The cache can store multiple `HashElements` for the same `CasElement`.  In the example this would correspond to different content for one or more C++ headers referenced by the compiler task.

  - The cache can also store multiple `CasElements` for the same `WeakFingerprint`.  In the example, this would correspond to different sets of C++ headers observed in different executions of this compiler task, which could occur if, say, one of the header files was re-factored into two files. 

  - The Memoization Store attempts to apply an LRU retention scheme to cached entries.  Each of the records depicted in the table above would have a distinct _TTL_ (Time To Live) value, which is extended by calls to `GetContentHashList` (though not by calls to `GetSelectors`).

  - A client that needs a simple _key->value_ model, can still used these APIs.  In this case the `WeakFingerprint` is sufficient as a key.  To call `GetContentHashList` or `AddOrGetContentHashList`, a `StrongFingerprint` can be "manufactured" by using that `WeakFingerprint` and hard-coded values for `HashElement` and `CasElement`(typically these would be hard-coded to an empty byte-array and the content id for the zero-length blob, respectively). No calls to `GetSelectors` are needed.
  
## Physical Architecture
The BuildXL cache implements a layered architecture, which allows for local and distributed caching.

- At a minimum BuildXL runs with a **local cache**, which uses configurable directories on disk to retain Memoization Store and Content Store data across different BuildXL invocations.  Disk utilization and data retention are managed automatically based on configuration settings.  Whenever possible, hard-links are used to "copy" data into the cache, and back out into the build "work directories".  On Windows, Access Control Lists [ACLs] are placed on he files to avoid clobbering their contents. On macOS, the [APFS](https://developer.apple.com/support/apple-file-system/Apple-File-System-Reference.pdf) copy-on-write feature is used instead.

- The local store can be tiered with **distributed cache**, which stores both the Memoization Store and CAS data into the "cloud", via a set of APIs implemented in the Azure DevOps service.  
**NOTE**: As of this writing, the Azure DevOps APIs are not public, and can only be used for internal Azure DevOps accounts.  The API client code can still be found in this repo, in classes using the 'VSTS*' prefix, moniker for 'Visual Studio Team Services', Azure DevOps's earlier name.

- When deployed within [CloudBuild](https://www.microsoft.com/en-us/research/wp-content/uploads/2016/06/q_signed-2.pdf), Microsoft's internal build service, there is also a **datacenter CAS** component, which allows multiple per-node CAS services to track and retrieve each other's content, effectively implementing a "content mesh."  Most of this implementation can be found in this repo, though as of this writing we don't expect it to be used outside of the confines of Microsoft.