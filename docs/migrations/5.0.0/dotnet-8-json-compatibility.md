# Update the .NET Standard JSON dependency

**Version introduced:** 5.0.0

The .NET Standard package asset now depends on System.Text.Json 11, dropping
runtime compatibility with .NET 8 and .NET 9.

## Previous behavior

The `netstandard2.0` package asset depended on System.Text.Json 10 and was
tested with .NET 8 consumers.

## New behavior

The `netstandard2.0` asset depends on System.Text.Json 11, which does not
support .NET 8 or .NET 9. The package continues to include its `net10.0` asset.

## Type of breaking change

This is a runtime compatibility change for consumers using the .NET Standard
asset on unsupported .NET runtimes.

## Reason for change

The JSON dependency is updated to version 11, which no longer supports those
out-of-support runtimes.

## Recommended action

Upgrade applications using .NET 8 or .NET 9 to a supported .NET runtime before
upgrading the package.

## Affected APIs

Applications consuming the `netstandard2.0` package asset on .NET 8 or .NET 9.
