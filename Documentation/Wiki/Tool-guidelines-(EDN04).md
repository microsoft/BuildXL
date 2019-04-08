This used to be called EDN04 - Tool Guidelines

# Abstract
We present here a set of guidelines and best practices for the design of tools that are intended to be invoked by the BuildXL build system. These guidelines are designed to ensure correctness and high performance.

# Introduction 
The job of the BuildXL build system is to coordinate the execution of a number of tools. In order to ensure correct and efficient operation, BuildXL imposes some constraints on how tools behave and what they are allowed to do. Although BuildXL accommodates several deviations from these guidelines in order to tolerate legacy tools, tool authors are strongly encouraged to follow these guidelines in order to deliver the best experience possible to end-users of the system.

## Context
This document is currently geared towards the issues of creating BuildXL-friendly tools for the Windows platform. Most of the principles are applicable to Mac and Linux.

## Six Principles for Great Build Tools 
This section presents a set of non-normative high-level principles which lead to the creation of efficient, reliable, and flexible build tools. These principles transcend the scope of BuildXL proper, they’re just generally good ideas to adopt when writing tools. 

This section is about alerting tool authors to issues to consider when creating or updating tools. We deal here specifically in high-level terms, whereas follow-on sections are prescriptive and generally more specific and actionable. 

### Determinism and Idempotence 
Any tool that participates in a distributed execution environment must be deterministic, idempotent and re-startable to allow the system to be failure tolerant. In other words, given a particular set of input files a tool should produce the same exact outputs on every use.

Deterministic tool behavior allows for known good states. Unfortunately many existing tools violate this simple property. For example, some code generators may generate semantically equivalent but physically different code due to different thread schedules. Others don’t initialize memory, so when they write files, the resulting files contain random bits. While this non-determinism doesn’t matter for a one time use of an artifact, it makes it difficult to integrate it into a modern build system which uses content-based hashing for detecting whether things have been built earlier; but with non-deterministic bits inside an output artifact you get random hashes -- it requires special-purpose hashing to compensate.

Idempotence is the property of an operation where it can be applied multiple times without changing the result generated from the first application and producing the same result for subsequent invocations, provided the inputs are the same. Side-effects that can impact subsequent invocations must be avoided. A common violation of this principle is that some tools rewrite artifacts in place. This makes incremental builds difficult, since you don’t know in which state the file is in -- the pristine unmodified or the rewritten state.

Distributed build systems need to be resilient to faults and implementing this systemic fault tolerance relies on tools being idempotent and re-startable. Since source artifacts and tools are kept in version control systems, BuildXL can regenerate the intermediate and output files in case they are lost due to a component failure in the system. Idempotence lets us deal with partial knowledge: while you might not know exactly what system state you are in when things failed, you can always redo, and if you have deterministic tools, you might be able to redo the operation simply by checking whether the resulting output exists already. 

### Environment Independence 
Portability of artifacts across different environments is a fundamental building block for distribution and multi-tenancy. There are two considerations for environment independence – computing environment (single-box, server cluster, and the cloud) and user environment (paths, environment variables, registry settings, etc.).

Tools often make assumptions about the computing environment such as running with elevated (super-user/administrator) privileges, file system, or logging infrastructure. Tools must be designed to be able to work in different computing environments by encapsulating and abstracting the services that are provided by the computing environment. For instance, many tools use timestamps as a way to determine whether derived artifacts are out of date. While this is a reasonable approach on a single box, timestamps are inherently unreliable in a distributed system, where we contend with clock skews and drifts.

Tools often embed environment related information, such as time, absolute paths, and GUIDs, in their output artifacts, eagerly binding to the environment, and making it difficult to move their output artifacts across different user and machine environments. A typical example is symbol information within PDB files. This eager binding to the environment not only prevents portability of the persisted artifacts across machines but the reliance on absolute paths on the file system instead of relative paths may prevent multitenant executions of the tool. 

### Consistency of Tools 
Given that engineers and builds use a multitude of tools, we need to have a consistent way to deal with their behavior to learn about, invoke, handle failures, debug, aggregate logs, and manage their output. Since these tools participate in a workflow that, for builds, is coordinated by an orchestration system, it is important to have consistent command-line options and parsing, help documentation, failure modes, return codes, activity logging and error reporting to keep the overall system simple.

For automatic workflows, it is essential that tools provide a “quiet” mode of operation that avoids requiring user prompts and responses, a “force” option where actions such as overwriting files are done quietly, the ability to specify logging to a file, etc. In particular, with respect to logging, tools should be able to operate in different execution environments with proper decoupling through logging providers and configurable error reporting that can be aggregated at the system level. 

### Composability and Self-Description 
Engineering tools should be composable, i.e. be able to participate in a “pipeline” where the output of one tool is the input for another. This composability is evident in most of the command-line UNIX utilities where one can pipe the output of a tool into the input of another or in Windows PowerShell objects where they can be integrated together. Complex scenarios can be addressed by chaining of relatively simple tools that perform a single task really well. Tools that try to address the end-to-end workflow in a monolithic manner, make reuse difficult.

Tools that describe themselves through a description language or tool manifest about its capabilities, requirements, and architectural limitations are easier to (re-)use correctly and can be used more efficiently. For example, there have been tools that depended on the ETW where there is an inherent limitation on the number of concurrent sessions that are possible on a single machine. Without a self-description about these limitations, use of these tools will result in unpredictable behavior when the operational limits are reached. In fact, we ran into exactly that problem. We meanwhile require tools to be self-describing. This allows for tools to be used as building blocks by automated brokers, such as BuildXL, which is now able to spawn and throttle tools respecting the currently available resources of the system. 

### Scalability 
Scalability is the motivation for moving tools to a cloud-based engineering system. If the tool does not scale well, it precludes itself from being used.  
Respect Amdahl’s law. There are several tools that optimize performance by running several tasks (with different inputs and outputs) within a single process. While this reduces communication and process start-up overhead, it impedes distribution of the tasks across multiple processes by an orchestration engine. As a consequence, the sequential execution of these optimized tasks is much slower than the parallel execution of the same non-optimized tasks. Tool developers have to rethink and embrace Amdahl’s law, which states that the speedup of a program using many processors is limited by the time needed for the sequential fraction of the program. So let’s minimize the sequential fraction of a build’s execution, even if overall resource usage might increase.

Linear (and sub-linear) time algorithms. Many tools make assumptions about the size of the inputs that are reasonable for human-authored programs. However when inputs are machine-generated these assumptions are often violated. In fact, in the event that the tool does not have linear or sub-linear time algorithms or uses excessive space, these machine generated inputs often wreak havoc on the performance of these tools. Some tools execute multiple tasks in sequence within a single process and for each task execution, they append to a log by first opening the file, reading it fully, appending new data, and writing out the file. This quadratic time behavior may not be noticeable for most “normal”-sized task sets.  But, in the face of large numbers, particularly from machine-generated input, this can dramatically slow down execution. Some tools, like parser generators, that produce “monster” methods with tens of thousands of case switches can bring compilers and linkers to their knees, highlighting the fact that unintentionally non-linear algorithms have been used.  

### Metrics, Measurability, and Control 
One of the key aspects of having an effective system is to be able to observe and reason over the effectiveness of the different participants in the system. Taking control of any entity requires you to manage it, which implies that you can monitor its effectiveness, which in turn implies that you can measure its function. In order to take control of the engineering tools, we need to be able to accurately measure the right metrics about their function in a repeatable manner. Further, to measure the effectiveness of a tool, we need to be able to have the right analysis tool to observe the tool’s metrics under different conditions. 

## Other Guideline Documents 
Other teams have put together excellent guidelines for their tools. Although the current document stands on its own, it’s still useful to browse through these other guidelines in order to gain additional context: 
* [Guidelines for Creating Build Tools](http://windowssites/sites/winbuilddocs/Wiki%20Pages/Guidelines%20for%20Creating%20Build%20Tools.aspx) 

# Guidelines 
This section provides a set of guidelines and best practices to help tool authors produce high-quality well-integrated tools. Tool developers, who expect their tools to be on-boarded in Microsoft’s internal product builds, MUST meet or exceed these guidelines. Tool owners who are targeting non-Microsoft internal product builds should be cognizant of the impact on build times, resources and the risks to build quality that may be incurred if these guidelines are not followed. 

## Command-Line Interface 
All tools invoked by BuildXL must be designed with a command-line interface. All interaction with tools is done by supplying command-line arguments, environment variables, and input files. Once a tool has successfully run, BuildXL assumes its output files have been produced. 

:white_check_mark: Tools SHOULD have command-line interfaces using standard C-like semantics for command-line handling and quoting. This refers to the general composition of command-lines where arguments are separated by spaces with double-quotes around arguments with spaces in them. This is not about enforcing a particular convention for / or – for options in command-line, BuildXL easily handles all those variations. 

:white_check_mark: Tools SHOULD support the use of response files to supply startup options using a format compatible with the C# compiler’s use of response files. Response files are text files which contain the arguments to a tool, one per line. Such files are dynamically generated by BuildXL tool runners (See 2.7 Tool Runners) to overcome the limit to the length of command-lines supported by Windows. 

:white_check_mark: Tools MUST use explicit command-line or response-file options for startup options (See ?????) 

:white_check_mark: Any configuration/response files the tool requires MUST be referenced on the command line or supplied as inputs in the associated runner’s template/spec file(s) (See  ???? ie; .ini, .cfg, etc) MAY be pointed to from a response file specified on the command line.  

:white_check_mark: Tools SHOULD produce errors and warnings in the canonical format as described below. 

:white_check_mark: Tools SHOULD report a failure for invalid command-line arguments.  

:white_check_mark: Tools SHOULD return zero to the environment in order to indicate success and non-zero to indicate a failure. When success is reported, BuildXL assumes that all expected outputs have been produced and are valid. A failure is taken to mean that any outputs that have been produced should be discarded. 

:white_check_mark: Tools SHOULD send error and warning messages to their stderr output stream. 

:no_entry: Tools SHOULD NOT produce any console output upon successful execution, or SHOULD at least provide a command-line option to disable non-error output (/nologo). 

:no_entry: Tools MUST NOT prompt the user for input during execution or MUST have an option to explicitly disable such prompts. All input SHOULD be provided through command-line parameters when the tool starts. Input MAY also be provided through environment variables, but this is discouraged. 

:no_entry: Tools MUST NOT use any kind of GUI popup during execution or MUST have an option to explicitly disable such popups. Popups can be highly distracting to users trying to do background builds. 
 
### Error Reporting 
Tools should report errors in a manner that is understood by common error and warning reporting tools, including both BuildXL and Visual Studio. The error and warning format recognized by BuildXL is used to determine what to color in red (errors) and what to color in yellow (warnings). The canonical format is: 

``` 
<source file>(<line>,<column>): <”error”|“warning”> <two/three-letter tool identifier><numeric ID code>: <descriptive text> 
```

So for example: 

```   
C:\sources\myfile.cs(18,1): error CS0149: missing semicolon  
C:\sources\myotherfile.cpp(19,32): warning CL1234: inconsistent type 
```

Although the above is the prescribed format for integration with BuildXL and with Visual Studio, BuildXL uses a simple regex-based approach to identify errors and warnings and so can tolerate quite a range of output formats successfully. Visual Studio however is not as tolerant of random output formats and so may not allow a user to double-click on an error and be put on the right source location. 

## Deterministic Outputs 
Tools are said to be deterministic if the outputs they produce are fully determined by: 

* The command-line arguments given to the tool. 
* The set of files provided as input. 
* The specific tool binaries in use. 

Determinism is a fundamental required property of every tool. Without deterministic behavior, tools generate unreproducible outputs. BuildXL depends on deterministic behavior in order to deliver fully reproducible builds. Tools with non-deterministic outputs do not impact the correctness of BuildXL builds. The ability to reproduce exact build artifacts from a given source input is all that is lost. 
Some tools or formats are designed to be non-deterministic. For example, signing often may require acquiring a timestamp from a server and applying it to the artifact. This is acceptable in the BuildXL environment, it merely implies that the particular artifact cannot be reproduced with full fidelity in a subsequent builds. 

:white_check_mark: Tools SHOULD output files whose content is normalized. Some file formats are agnostic to the ordering of different blocks of data within them, and some tools may have internal non-deterministic behavior (such as best-effort based multithreading protocols), and so can legitimately produce physically non-deterministic output that is correctly logically deterministic. Non-deterministic outputs prevents the production of reproducible builds which induces many downstream complications. 

:white_check_mark: Tools MUST fully initialize data output to files. Some file formats have undefined and/or padding areas. It’s important for these areas to be initialized with well-known predictable state.  

:no_entry: Tools MUST NOT embed manufactured timestamps in generated files, except for diagnostic log files. If some build artifacts need to be stamped with a date, this date needs to be captured in a source file and be manually updated before initiating the build. Features like the C compiler’s __DATE__, __TIME__, and __FILE__ features which provide explicit non-deterministic output must be controllable via command-line options such that the tool’s output can be fully controlled. As mentioned above, some formats and procedures may require embedding timestamps for security reasons. In such a case, this guideline can be ignored.  

:no_entry: Tools MUST NOT embed manufactured GUIDs in generated files. A possible replacement for this approach is to embed a deterministic content hash over an input to the tool instead of a generated GUID. These hashes are fully reproducible and their value varies based on precise input to the tool. 

:no_entry: Tools MUST NOT produce unpredictably-named outputs. The only exception is when producing temporary files within the %TMP% directory. 

## Machine Independent Outputs 
BuildXL employs a distributed cache whose purpose is to share build artifacts with any machine doing a build. In order to allow this cache to work, build artifacts must be agnostic to the machine they were built-on. Including machine-specific details in generated output defeats caching and can dramatically increase the experienced build time across an organization as a result. 

:white_check_mark: Tools MUST use content hashing instead of path names in order to reference other files within any generated files. For example, PDB files should use content hashing and relative paths to identify source files instead of GUIDs and local absolute paths.  

:no_entry: Tools MUST NOT embed local path names in any generated file, except for diagnostic log files. 
 
:no_entry: Tools MUST NOT embed any environment block information in any generated file, except for diagnostic log files. 

:no_entry: Tools MUST NOT embed information about the user (ie; user tokens/passwords etc) in any generated files, except for diagnostic log files, which MUST never contain passwords. 

## I/O 
In order to deliver efficient and reliable incremental builds, BuildXL depends on having a deep understanding of how tools consume and produce files. It’s critical that input files be considered immutable and that any output produced be directed to specific output files. 

:white_check_mark: Tools SHOULD use explicit command-line arguments to control the path of all input and output files.  

:white_check_mark: Tools MUST open all input files in a non-exclusive read-mode. Other tools and other instances of your tool might need to read the same input file in parallel and would fail if the file were locked. The only exception to this rule is for tools that perform read-modify-write operations on existing files (which is not a best practice).  

:white_check_mark: Tools MUST store temporary files in the %TMP% directory. 

:white_check_mark: Tools MUST use unique names for temporary files such that your tool can run concurrently with other instances of itself. 

:white_check_mark: Tools SHOULD delete temporary files before exiting. A good choice is to set the bit indicating that a file should be deleted upon being closed, which prevents leftover temporary files if a tool terminates unexpectedly, and is also generally more efficient. 

:no_entry: Tools SHOULD provide command-line/response-file option(s) to direct diagnostic output(s) to a specified file or directory. Tools SHOULD NOT write to an input file. Instead, tools SHOULD expose command-line options to enable output files to be specified explicitly. 

:no_entry: Tools MUST NOT use alternate NTFS streams.
 
:no_entry: Tools MUST NOT open file with the FILE_FLAG_OPEN_REPARSE_POINT option. 

:no_entry: Tools MUST NOT store temporary files in the source directory, instead tools SHOULD store temporary files in the %TMP% directory. 

## Deployment 
In general, due to the great diversity of environments in which a given build can take place - different developer machines, different lab machines, with different versions of the OS/CLR installed, different patches present, different SDKs, etc/ -  it is necessary for tools to use the so-called “xcopy install” deployment technique such that it is not necessary to run an install step in order to make a tool usable. 

:white_check_mark: Tools SHOULD be as self-contained as possible, with dependencies limited to other components expected to be directly available without being installed explicitly. 

:white_check_mark: Tools MUST allow side-by-side execution with other versions of the same tool, perhaps in two different enlistments or in a single enlistment. 

:white_check_mark: Tools MUST allow multiple instances of itself to run concurrently on a single machine. 

:no_entry: Tools SHOULD NOT read or write to the registry. Accept all options from the command-line or response file instead.

:no_entry: Tools SHOULD NOT require elevated privilege in order to work. 


## Parallelization 
BuildXL automatically schedules activities that can run in parallel to run in parallel. It’s useful to coordinate a tool’s own internal parallelism with what BuildXL provides in order to avoid over-subscribing the build machine which can lead to paging or trashing and ultimately hurt overall throughput. 

:white_check_mark: Tools that are internally parallel SHOULD provide command-line options to throttle the internal parallelism of the tool. This is desirable since BuildXL may wish to reduce internal per-tool parallelism in exchange for broader scale parallelism. Conversely, if BuildXL determines that it can provide little broad parallelism, it may crank up the internal parallelism of individual tool invocations. 


## Tool Runners 
In a BuildXL build, all tools are associated with a runner (a kind of Transformer).  While teams are currently tasked with providing runners for existing tools used internal to Microsoft, tool owners will eventually own them and be required to maintain them.  In the future, tool owners will be required to provide at least one runner for their new tools. 

:white_check_mark: Tools MUST have a runner to launch them in the BuildXL 
environment. 

:no_entry: Tool runners MUST NOT allow non-compliant features of the tool to be invoked. 

:white_check_mark: The runner MUST explicitly control all tool inputs and outputs so that BuildXL can track them. 


## Backward Compatibility (Microsoft Tools) 
Owners of tools that have a long history of use in other build environments need to weigh carefully how they adapt their tool to be compliant with these guidelines. All tools must have a runner associated with them in the BuildXL build environment.  This can be used to prevent non-compliant features from being invoked in build specs. In such cases it may be possible to achieve compliance without modifying the tool directly.  In other cases it will require a blend of feature control via runners and modifications to the tool. 

:no_entry: Tools MUST NOT be broken in existing non-BuildXL build environments as a result of changes made to meet the other guidelines in this document. Nothing in this document should be construed as requiring a breaking change in the context of other build environments. 


## Compliance (Microsoft Tools) 
Keep in mind that a few of the MUST clauses in this document may be over constrained for some legacy tools.  Exceptions will be granted for legacy tools in the short-term, provided tool runners can provided adequate workarounds, but exceptions will imply debt to be resolved as fixed at the earliest possible time. 


# Future 
The tool ecosystem would benefit from library support in order to standardize the various aspects of their behavior: 

* Standard command-line parsing framework. 
* Standard error and warning management framework. 
* Standard “tool as a service” framework to enable more efficient binding to the build system. 
* Standard support for response files. 
* Standard reliable support for temporary files. 

The BuildXL team may deliver libraries in the future that embody the guidelines of this document, but for the time being it’s up to individual tool developers to explicitly follow the guidelines. 
