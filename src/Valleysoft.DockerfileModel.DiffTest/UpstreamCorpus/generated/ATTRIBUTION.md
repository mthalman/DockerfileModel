# BuildKit parser corpus attribution

Source: https://github.com/moby/buildkit/tree/dddd5621af04ea57823085c93a063383f71d3173/frontend/dockerfile/parser

Dockerfile frontend release: dockerfile/1.27.0

BuildKit source commit: dddd5621af04ea57823085c93a063383f71d3173

Upstream material is distributed under the preserved Apache License 2.0 in LICENSE.
No applicable upstream NOTICE or parser-subtree license override was found at this pin.
The fixture and golden audit found no embedded copyright or license notices.
Exact original bytes, including comments, remain under sources/ using the first
16 lowercase SHA256 hex characters of the UTF-8 upstream path, plus .source.
Full original paths remain in case provenance and the coverage inventory.
The sources/ tree also preserves upstream Go test files used for the coverage inventory.

Modification notice: cases/*.json are generated, extracted instruction representations
of the upstream Dockerfiles, not upstream AST goldens. coverage.json and manifest.json
are generated provenance/coverage metadata. No upstream Dockerfile commands are executed.
See tools/BuildKitCorpus/README.md for scope, exclusions, and regeneration.
