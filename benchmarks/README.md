# Benchmarks

Run the alignment benchmarks with:

```bash
dotnet run --project benchmarks/openmedstack.biosharp.benchmarks/openmedstack.biosharp.benchmarks.csproj -c Release
```

Current harness includes:

- `AlignShortReadAgainst1KbReference`
- `ProcessReadAgainstIndexedLargeReference`