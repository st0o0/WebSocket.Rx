# CLAUDE.md

@AGENTS.md

## Skill routing

### Plugin skills — when to load

| Task | Load these skills |
|------|-------------------|
| R3 / reactive patterns | `dotnet-skills:r3-reactive-extensions` |
| Concurrency bugs, race conditions, flaky tests | `dotnet-skills:csharp-concurrency-patterns` |
| Performance analysis, benchmark results | `dotnet-skills:dotnet-performance-analyst` agent |
| Benchmark design | `dotnet-skills:dotnet-benchmark-designer` agent |
| C# coding standards | `dotnet-skills:csharp-coding-standards` |
| Public API design / compatibility | `dotnet-skills:csharp-api-design` |
| Type design for perf | `dotnet-skills:csharp-type-design-performance` |
| Nullable reference types | `dotnet-skills:csharp-nullable-reference-types` |
| NuGet packaging, project layout | `dotnet-skills:package-management`, `dotnet-skills:project-structure` |
| Serialization | `dotnet-skills:serialization` |
| OpenTelemetry | `dotnet-skills:opentelementry-dotnet-instrumentation` |
| Code simplification | `code-simplifier` agent |
| Security review | `security-review` skill |
| Slopwatch quality gate | `slopwatch` skill |
| Code review | `code-review` skill |

