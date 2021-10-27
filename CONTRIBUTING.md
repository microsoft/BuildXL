# Before you start
This project is open source primarily to share knowledge about some of the build technologies used within Microsoft. You are welcome to take inspiration from and utilize the code as allowed by the license agreement.

Development activities are performed on internal systems and code is regularly mirrored to this GitHub repo. Many validations, particularly performance and compatibility validations, can only be performed internally since most of the codebases using BuildXL are proprietary. External contributions need to be committed internally and then mirrored back out to the GitHub repo. 

Due to the overhead associated with this process, we ask that contributions are limited to substantial changes as opposed to cleanups like fixing typos. We highly encourage creating an Issue prior to sending a Pull Request so the proposed change can be discussed prior to submission. We very much value the open source community and want to be respectful of people's time and interest.

If you are a Microsoft employee, please make your contribution to the BuildXL.Internal repository.

# Contributing to BuildXL
Most contributions require you to agree to a Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions provided by the bot. You will only need to do this once across all repos using our CLA.

The Code of Conduct this project has adopted is described in: [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Submitting Pull Requests

- **DO** ensure your contribution has associated unit tests.
- **DO** ensure running RunCheckinTests.cmd passes
- **DO NOT** submit build engine features as PRs to this repo first, or they will likely be declined.
- **DO** submit issues for features. This facilitates discussion of a feature separately from its implementation, and increases the acceptance rates for pull requests.
- **DO NOT** submit large code formatting changes without discussing with the team first.

These two blogs posts on contributing code to open source projects are good too: [Open Source Contribution Etiquette](http://tirania.org/blog/archive/2010/Dec-31.html) by Miguel de Icaza and [Don’t “Push” Your Pull Requests](https://www.igvita.com/2011/12/19/dont-push-your-pull-requests/) by Ilya Grigorik.

## Developer Guide
When you are ready to proceed with making a change, get set up to build the code (see the [Developer Guide](Documentation/Wiki/DeveloperGuide.md)) and familiarize yourself with our developer workflow. 

## Creating Issues

- **DO** use a descriptive title that identifies the issue to be addressed or the requested feature. For example, when describing an issue where the compiler is not behaving as expected, write your bug title in terms of what the compiler should do rather than what it is doing – “C# compiler should report CS1234 when Xyz is used in Abcd.”
- **DO** specify a detailed description of the issue or requested feature.
- **DO** provide the following for bug reports
    - Describe the expected behavior and the actual behavior. If it is not self-evident such as in the case of a crash, provide an explanation for why the expected behavior is expected.
    - Provide example code that reproduces the issue.
    - Specify any relevant exception messages and stack traces.
- **DO** subscribe to notifications for the created issue in case there are any follow up questions.
