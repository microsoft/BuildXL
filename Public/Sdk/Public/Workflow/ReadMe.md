# Workflow SDK

The workflow SDK provides abstractions that simplify build logic in DScript. Instead of modelling the build logic as low-level
processes and file/directory dependencies between them, the SDK allows for expressing the build logic as dependencies
between high-level tasks and projects. The SDK is introduced to lower the cognitive load when the user needs to write and
maintain DScript specifications when onboarding to BuildXL.

Creating a task can be as simple as specifying the tool that the task will execute and the arguments passed to the tool.
The SDK hides the complexity of how the task is interpreted into one or more pips. Because 
users are used to thinking about task order dependency, instead of file/directory dependency, when writing their build specifications,
the SDK allows for a task to directly take another task as a dependency without knowing what files/directories the latter task produces. 

The project abstraction provided by the SDK allows for users to organize their build into projects, and run their specified workflow
on each of the project. The project data includes project dependencies that the workflow can use to establish finer-grained task dependency
when a task in a workflow depends on the result of building other projects. For example, a unit test task typically depends not only
on the compile task of the same project, but also on the compile task of dependence projects.

Learn more about the workflow SDK [here](Docs/Workflow.md).


