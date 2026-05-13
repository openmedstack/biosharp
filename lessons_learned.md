# Lessons Learned

## HGVS Parsing — Reference vs Version Separator

**Context**: `HgvsVariant.Parse` must extract the reference sequence accession number and its
optional version from strings like `NM_004006.2:c.4375C>T` and also handle gene-name
references without a version like `BRCA1:c.100A>G`.

**Lesson**: The dot (`.`) in a full HGVS string is used twice:
1. Between the accession number and its version: `NM_004006.2`
2. At the start of the description type prefix: `c.` / `g.` / `n.`

Using `IndexOf('.')` to find the version dot will grab the *first* dot in the string — which
is wrong when the reference has no version (the first dot will be inside the description).

**Fix pattern**: Always find the colon first, then check whether the dot appears *before* the colon:

```csharp
var colon   = input.IndexOf(':');
var dot     = input.IndexOf('.');
var hasVersionDot = dot >= 0 && dot < colon;
var reference = hasVersionDot ? input[..dot] : input[..colon];
```

---

## HGVS Position Notation — More than just integers

HGVS positions are not always a simple integer. The full syntax is:

| Notation      | Meaning                                             |
|---------------|-----------------------------------------------------|
| `100`         | Coding position 100                                 |
| `-100`        | 100 bases *before* the coding start (5' UTR)        |
| `*100`        | 100 bases *after* the stop codon (3' UTR)           |
| `100+2`       | 2 bases into the intron after coding position 100   |
| `100-1`       | 1 base before coding position 100 (intronic)        |

**Regex pattern** covering all of the above: `[-*]?\d+(?:[+-]\d+)?`

Use this pattern inside all HGVS description regexes to be spec-compliant.

---

## Single-Base Deletion Format

HGVS uses `c.4375del` for a single-nucleotide deletion — no range is written. Multi-base
deletions use a range: `c.4375_4379del`.

The `DeletionRegex` must therefore make the `_to` range optional. If `to` is absent, set
`to = from` for a well-formed model:

```csharp
var toValue = string.IsNullOrEmpty(match.Groups["to"].Value) ? fromValue : match.Groups["to"].Value;
```

---

## Insertion — Always Between *Adjacent* Positions

Per HGVS nomenclature, an insertion is placed *between* two consecutive positions:
`c.4375_4376insACCT`. Using the same position twice (`c.4375_4375ins...`) is invalid.

**Fix in `BuildHgvsCoding`**:

```csharp
// Wrong:  $"c.{cPos}_{cPos}ins{insSeq}"
// Correct:
return $"c.{cPos}_{cPos + 1}ins{insSeq}";
```

---

## Non-Coding Variants — Use Standard Uncertain-Position Notation

Custom suffixes like `spl`, `spl_dis`, `crypt` are not HGVS notation and will fail parsing.
Use the standard uncertain-position marker `?` for all non-coding or uncertain variants:

| Consequence      | Generated notation |
|------------------|--------------------|
| Upstream         | `c.-{pos}?`        |
| Downstream       | `c.*{pos}?`        |
| Intergenic       | `n.{pos}?`         |
| All others       | `c.{pos}?`         |

These all match the `UncertainPositionRegex` after updating it to use the extended position
pattern `[-*]?\d+(?:[+-]\d+)?`.

---

## `UncertainPositionDescription.Position` Should Be a String

Because HGVS positions can contain `-`, `*`, and `+/-` offsets, storing `Position` as `int`
is wrong. Change the record property to `string Position` so complex positions are preserved
without information loss.

---

## Do NOT Use the `Async` Suffix on Async Method Names

Methods that return `Task` or `Task<T>` (or `ValueTask` / `ValueTask<T>`) should **not** have
an `Async` suffix appended to their name. The suffix adds noise without adding information —
callers already know the method is asynchronous from its return type and the `await` keyword.

**Rule**: Only keep the `Async` suffix when it is *required* by a
framework or external contract, specifically:
1. The method is an **overload** of a non-async version with the same name (so the suffix
   disambiguates the two).
2. The method is an **explicit implementation** (or required override) of an interface or base
   class member whose name already carries the `Async` suffix.

In all other cases, drop the suffix:

```csharp
// Wrong
public Task<Alignment> AlignAsync(string query, CancellationToken ct) { ... }

// Correct
public Task<Alignment> Align(string query, CancellationToken ct) { ... }
```

## In-Memory Pipeline Stages — Avoiding File Round-Trips

**Context**: The `e2e` command needed to run variant calling followed by annotation
without writing VCF to disk and reading it back.

**Lesson**: When connecting pipeline stages that were originally designed around files,
the cleanest approach is:
1. Keep the file-writing step intact (for auditing / downstream consumers).
2. Also expose an in-memory path by extracting a method that accepts
   `IAsyncEnumerable<T>` (or `IReadOnlyList<T>`) of the domain objects.
3. Convert between the upstream domain type (`LocalVariantResult`) and the downstream
   domain type (`VcfVariant`) with a small private helper that yields from an
   `IAsyncEnumerable` — this preserves backpressure and avoids materialising the
   full list again.

**Pattern**: extract a `RunWithVariants(IAsyncEnumerable<VcfVariant>, ...)` overload
on the command class. Use a private `ToVcfVariants(IReadOnlyList<LocalVariantResult>, ...)`
async iterator for the conversion, using `[EnumeratorCancellation]` and `await Task.CompletedTask`
to satisfy the compiler for synchronous yields inside an async iterator.

**Access modifiers**: Helper methods called from a sibling command must be `internal`
(not `private`). Change `private static` to `internal static` for `ProcessFasta` /
`ProcessFastq` in `AnalysisCommand` when `E2ECommand` needs to reuse them.

**Disposable resources**: When wrapping an EF Core `DbContext` for use across a pipeline
stage, define an `IAnnotationDatabaseHandle : IAsyncDisposable` interface and a private
implementation that holds the context so callers can `await using` it safely.
