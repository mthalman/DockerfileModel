# Dockerfile Model Library

This .NET library provides a structured model of the Dockerfile syntax for the purposes of parsing and generating Dockerfiles. It provides full fidelity of the file contents meaning that the parsed content can be output back to a string and produce the same content character-for-character, including whitespace. This makes it ideal for parsing as well as workflows that require programmatically modifying existing Dockerfiles.

## Usage

The library is available as a NuGet package: [Valleysoft.DockerfileModel](https://www.nuget.org/packages/Valleysoft.DockerfileModel/).

For code examples, check out the [scenario tests](https://github.com/mthalman/DockerfileModel/blob/main/src/Valleysoft.DockerfileModel.Tests/ScenarioTests.cs) which demonstrate how the API can be used for various scenarios.
