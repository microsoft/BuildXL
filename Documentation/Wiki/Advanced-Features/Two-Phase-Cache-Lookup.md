# Two Phase Cache Lookup

BuildXL needs to support non-deterministic build systems with weakly specified dependencies.
In order to do this, BuildXL has a two-phase caching algorithm. This page walks through the
behavior of this algorithm.

For more information on the technical implementation of this design, see [this documentation of the
BuildXL caching system implementation](../../../Public/Src/Cache/README.md).

## Terminology

For precision, here are the terms used in describing the design.

### Deterministic & Hermetic BuildXL

First, the terms that apply to fully deterministic BuildXL graphs with fully specified dependencies:

- Pip: a single build step (for purposes of this discussion, a single process execution)
- Declared input / output files: files statically declared at BuildXL graph construction time as being
  inputs or outputs of the pip
- Fingerprint: the hashes of the command line, declared input file paths and contents, and declared
  output file paths for a pip

In a fully deterministic build with fully specified dependencies, the fingerprint of each pip only needs
to be computed from the fully statically known information about to the pip. So in this setting, there is only
one kind of fingerprint, which can ignore everything that was actually done by the pip -- since we know
upfront everything the pip will do.

### Non-deterministic, non-hermetic BuildXL

To work with legacy build systems, BuildXL introduces several new concepts.

- Declared input / output directories: Pips now can specify directories that they write to (and read from), without
  precisely specifying what files they will actually write (or read).
- [Shared opaque directories](Sealed-Directories/Shared-Opaque-Directories.md): Pips can write to output directories shared
  with other pips, such that the directory contents are dynamically populated, and not known at
  build graph construction time.
- Observed input / output file accesses: When writing to opaque directories, BuildXL must now track exactly
  which files were read or written. These sets of accesses are recorded by pathsets.
- Pathsets: Pathsets record sets of observed file accesses (reads, probes, directory enumerations) made by a specific pip.
- Weak fingerprint: The statically known inputs and outputs of the pip.
- Strong fingerprint: A combination of the pip's weak fingerprint's hash, and the hashes of the files read by that pip's pathset
  when it executed.

We now have the terminology to describe the two phase cache lookup algorithm itself.

## Phase 1: Weak Fingerprint Lookup

Consider the following running example of a (hypothetical) C++ project in a legacy source tree we want to run
under BuildXL.

Here's `burger.mk#1`. `burger.mk#1` denotes version 1 of `burger.mk`:
```
INPUTS = ​
   bread.cpp​
   patty.cpp​
   sauce.cpp​

INCLUDEDIRS =​
   /proteins

OUTPUT =​
   /dinner/burger.exe​
```

Here is the directory tree when we execute `burger.mk`'s pip:

- burger/​
  - burger.mk#1
  - bread.cpp#1
  - patty.cpp​#1
  - sauce.cpp​#1
- proteins/​
  - grnd_beef.h​#1
  - tofu.h​#1

### Weak fingerprint construction

When constructing the build graph, we examine `burger.mk` and we learn what we can about the files it reads and writes.
We use this information to construct the _weak fingerprint_ for `burger.mk`'s pip.

The weak fingerprint hashes the statically known input/output information for the pip.
In this case, it consists of this state, where hashes of files are always hashes of specific file
versions:

```
WF1:
- Hash(burger/bread.cpp#1)
- Hash(burger/patty.cpp#1)
- Hash(burger/sauce.cpp#1)
- Hash("CommandLine:cl.exe /option1 /option2")
- Hash("InputDirectory:/proteins")
- Hash("Output:/dinner/burger.exe")
```

`WF1` is the label we'll use for this weak fingerprint.

This is called the weak fingerprint because it is only a partial specification of the pip. In particular, it does not
specify all the files that might get read when making the burger.

All of this content is hashed to produce the weak fingerprint hash for the pip. The weak fingerprint hash changes
only when the statically known inputs/outputs of the pip change.

### Pathset tracking

Suppose that `patty.cpp` contains the following (and suppose that no other .cpp includes anything):

```
#include <grnd_beef.h>
```

When `burger.mk` is executed, `patty.cpp` will be read by the C++ compiler, and `proteins/grnd_beef.h` will be
dynamically read. This then will become a pathset recorded for this pip, indexed by the pip's weak fingerprint:

```
PS1:
- Hit(proteins/grnd_beef.h)
```

This is the additional information needed to make a more precise -- e.g. stronger -- fingerprint of this pip. Note that
pathsets list only pathname hits (or misses, as we'll see soon), _not_ actual file hashes or even file versions.

### Strong fingerprint construction

The _strong fingerprint_ for this execution of the `burger.mk` pip consists of both the hash of the weak fingerprint,
and the hashes of all file versions read in the associated pathset:

```
SF1:
- Hash(WF1)
- Hash(proteins/grnd_beef.h#1)
```

This fully represents the actual, concrete inputs to the executed pip. Hence, *this* information is
usable to perform cache lookups.

### Putting it all together

So, for this first-ever compilation of `burger.mk`, BuildXL does the following:

- Process the build graph to compute `WF1`.
- Look up all known pathsets for `WF1`
  - Determine that there are none
- Execute the pip, recording `PS1`
- Compute a new strong fingerprint for the pip, `SF1`
- Store the pip's computed output, `dinner/burger.exe`, as an output of `SF1`

If we go to re-execute a build, BuildXL will do the following:

- Process the build graph to compute `WF1`. (This is always deterministic so it gets the same answer on the same input.)
- Look up all known pathsets for `WF1`
  - Find `PS1`
- Determine whether that pathset matches the current known inputs (e.g. does `proteins/grnd_beef.h` exist in the current build)
  - In this case, it still does exist, so the pathset is a match
- Look up strong fingerprints based on the hashes of `WF1` and the matching pathset `PS1`
  - Match `SF1` because `proteins/grnd_beef.h#1` is still the version of that header file in this build
- Look up `dinner/burger.exe` as an output of the cached `SF1` pip

This is the essence of the two phase cache lookup:

1. Compute weak fingerprint from static information.
2. Find pathsets associated with that weak fingerprint, which match the payload of the current build.
3. Find strong fingerprints that match the weak fingerprint and the file versions in the matching pathsets.
4. Perform cache lookups for actual cached outputs, based on the strong fingerprints.

## Handling other changes in the source

It's now useful to consider how this design handles various other scenarios:

- What if a new header file directory is added?
- What if a conflicting copy of `grnd_beef.h` gets added in that new directory?
- What if the contents of `grnd_beef.h` are modified to include yet another file?

The design handles these cases, as we'll see.

### New header file directory

Suppose we add a new header directory `/organic` in `burger.mk#2`:

```
INPUTS = ​
   bread.cpp​
   patty.cpp​
   sauce.cpp​

INCLUDEDIRS =​
   /organic
   /proteins

OUTPUT =​
   /dinner/burger.exe​
```

We prefer organic ingredients so the new "organic" directory comes first. And we add it in the source tree:

- burger/​
  - *`burger.mk#2`*
  - bread.cpp#1
  - patty.cpp​#1
  - sauce.cpp​#1
- *organic/*
  - *tofu.h#1*
- proteins/​
  - grnd_beef.h​#1
  - tofu.h#1

We don't have any organic ground beef yet.

This results in a new weak fingerprint, since there's a new input directory in `burger.mk#2`:

```
WF2:
- Hash(burger/bread.cpp#1)
- Hash(burger/patty.cpp#1)
- Hash(burger/sauce.cpp#1)
- Hash("CommandLine:cl.exe /option1 /option2")
- Hash("InputDirectory:/organics")
- Hash("InputDirectory:/proteins")
- Hash("Output:/dinner/burger.exe")
```

When building, the C++ compiler will probe the `organic` directory looking for `grnd_beef.h` and will not
find it. The pathset computed in this case will be:

```
PS2:
- Miss(organic/grnd_beef.h)
- Hit(proteins/grnd_beef.h)
```

The "miss" entry records that no file existed at that attempted path lookup.

Now when BuildXL builds this pip, the following occurs:

- Since there is a new include directory, BuildXL computes `WF2`.
- No pathsets exist for this weak fingerprint, so BuildXL executes the pip and computes `PS2`.
- BuildXL computes `SF2` from `WF2` and `PS2` and associates the output with it:

```
SF2:
- Hash(WF2)
- Hash(proteins/grnd_beef.h#1)
```

### New header file in existing directory

We get some organic ground beef in stock, and the source tree becomes:

- burger/​
  - `burger.mk#2`
  - bread.cpp#1
  - patty.cpp​#1
  - sauce.cpp​#1
- organic/
  - *grnd_beef.h#1*
  - tofu.h#1
- proteins/​
  - grnd_beef.h​#1
  - tofu.h#1

Now when we next do a build, BuildXL does the following:

- Construct the weak fingerprint for the pip; since `burger.mk` didn't change, this yields `WF2` again.
- Look for pathsets matching `WF2`.
  - Find `PS2`.
  - Determine that `PS2` is not a match since `organic/grnd_beef.h` is no longer missing.
- Execute `burger.mk` determing pathset `PS3`:

```
PS3:
- Hit(organic/grnd_beef.h)
- Hit(proteins/grnd_beef.h)
```

(The C++ compiler sometimes probes for more files than it actually reads when compiling, so in this case
it will hit `proteins/grnd_beef.h` even though it's not actually included by the compilation.)

- Compute strong fingerprint `SF3` based on `WF2` and `PS3`, and associate the resulting `burger.exe` with it:

```
SF3:
- Hash(WF2)
- Hash(organic/grnd_beef.h#1)
- Hash(proteins/grnd_beef.h#1)
```

In general, changes to makefiles and static build information result in new weak fingerprints, and changes to
dynamic file accesses result in new pathsets; since both weak fingerprints and pathsets have to match in order
to match a strong fingerprint, this ensures that both static and dynamic changes get tracked through the cache.

### New contents of existing header file

Finally, suppose the developer modifies `organic/grnd_beef.h` to include another file:

```
#include "beef.h"
```

And this gets added in the source tree:

- burger/​
  - `burger.mk#2`
  - bread.cpp#1
  - patty.cpp​#1
  - sauce.cpp​#1
- organic/
  - *beef.h#1*
  - *grnd_beef.h#2*
  - tofu.h#1
- proteins/​
  - grnd_beef.h​#1
  - tofu.h#1

When we compile, BuildXL will now do the following:

- Compute weak fingerprint from `burger.mk#2` yielding `WF2` (since we still didn't change the makefile)
- Look up pathsets associated with `WF2`
  - Find pathsets `PS2` and `PS3`
    - `PS2` is not a match because `organic/grnd_beef.h` now exists
    - `PS3`, however, *is* a match
- Look up strong fingerprints associated with `WF2` and `PS3`
  - Find `SF3`
  - Determine that `SF3` is *not* a match because `organic/grnd_beef.h` changed (from #1 to #2)
- Ultimately re-execute the pip because of a cache miss
  - Compute a new path set:

```
PS4:
- Hit(organic/grnd_beef.h)
- Hit(organic/beef.h)
- Hit(proteins/grnd_beef.h)
```

  - Compute a new strong fingerprint:

```
SF4:
- Hash(WF2)
- Hash(organic/grnd_beef.h#2)
- Hash(organic/beef.h#1)
- Hash(proteins/grnd_beef.h#1)
```

  - Associate `dinner/burger.exe` with `SF4`.

## Tuning This Algorithm In Production

This algorithm works well to handle the multiple sources of potential change:

- Changes to the statically known build graph cause weak fingerprint misses.
- Changes to patterns of dynamic file access cause pathset lookup misses.
- Changes to dynamically accessed file contents cause strong fingerprint misses.

In production, the primary risk can be pathset lookup. It is possible for a weak fingerprint to be
*too* weak. If a large number of pips have only a small amount of statically known inputs, and
tend to read many of the same files at runtime, it can result in pathset explosion: the number of
potentially matching pathsets grows to a point that it degrades performance.

The primary fix for this issue (beyond tuning some pathset-related parameters in BuildXL's
configuration) is to make the weak fingerprints stronger, by adding more statically known inputs.
This results in fewer pathsets per weak fingerprint. BuildXL has also implemented an 'augmented
weak fingerprint' mechanism which associates common pathsets with weak fingerprints, for optimized
lookup.
