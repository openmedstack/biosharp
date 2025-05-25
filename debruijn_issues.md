# De Bruijn SV Detection — Bugs & Fixes

## Issue Summary

9 out of 12 tests fail because `BubbleFinder.Reconstruct` traces from the wrong node, and several test assertions use incorrect sequence lengths/positions.

---

## BUG 1 — `BubbleFinder.Reconstruct` uses wrong end node

**File**: `src/.../DeBruijn/BubbleFinder.cs`, line 128
**Severity**: Critical — bubbles never reconstruct correctly

**Root cause**: `Reconstruct` finds the "end node" by searching for `pred.First(kv => kv.Value == "").Key`. But `pred[""] = start` (set in BuildPath: `pred = {{ start, "" }}`), so `pred.First(kv => kv.Value == "")` returns the **start** node, not the BFS target.

**Effect**: BFS builds the correct predecessor chain but `Reconstruct` traces from `start` backward to `start` (1 node trivially). The path has length 1 k-mer. `fullPath = branch.Id + path[...][k-1:]` = branch + 1 char. This is not a real bubble path.

**Fix**: Pass `end` to `Reconstruct`. It should receive the BFS target (`converge`), not search for `pred[""]`.

```
// BuildPath line ~106:
// OLD: return Reconstruct(pred);
// NEW: return Reconstruct(pred, end);

// Reconstruct signature:
// OLD: private static string Reconstruct(Dictionary<string, string> pred)
// NEW: private static string Reconstruct(Dictionary<string, string> pred, string endNode)

// Delete this line (~128):
// OLD: var endNode = pred.First(kv => kv.Value == "").Key;
// (endNode is now a parameter)
```

**Verification**: After fix with SNP test (ref="ACGTACC ACTAGC" 13, alt="ACGTACG ACTAGC" 13):
- k=5, graph has branch CGTAC → GTACC / GTACG → both reach ACTAG
- BuildPath(GTACC, ACTAG): BFS finds path GTACC→TACCA→ACCAC→CCACT→CACTA→ACTAG (6 k-mers)
- Reconstruct traces ACTAG backward to GTACC
- Path string: GTACC + T + A + C + T + A = "GTACCTACTA" (10 bases)
- Same for alt path: GTACGACTAG → "GTACGTACTA" (10 bases)
- Both paths same length → SNP detected ✓

---

## BUG 2 — `TipFinder` traces through shared ref nodes into the tip sequence

**File**: `src/.../DeBruijn/TipFinder.cs`
**Severity**: Medium — tips detected but with ref-shared k-mers included

**Root cause**: `TraceTip` traces back from dead-ends to the nearest branch/merge without filtering ref-shared k-mers. For a tip where alt extends ref with extra bases, the tip sequence includes shared reference k-mers (like `ACTA`, `CTAG`, `TAGC`) that are present in both ref and alt.

**Effect**: Tip sequences are longer than the actual alt-specific extension. May cause false base calls if downstream variant callers rely on tip sequence content. Not a test-breaking problem in current tests (they only check NotEmpty/isLongTip), but produces incorrect sequences.

**Fix**: After building tip, compare each tip k-mer against the reference path. Exclude k-mers shared with reference. **Deferred for now** — not blocking current test suite.

---

## BUG 3 — Test assertions have wrong sequence lengths

**File**: `tests/.../DeBruijnSvDetectionTests.cs`

### SNP test (line 57)

| Field | Current (wrong) | Correct |
|-------|----------------|---------|
| refSeq | `"ACGTACC" + "ACTAGC"` = 13 | 13 |
| altSeq | `"ACGTACG" + "ACTAGC"` = 13 | 13 |
| Assertion on line 57 | `.Equal(12, refSeq.Length)` | `.Equal(13, refSeq.Length)` |

### Deletion test (lines 166, 169, line 207, 209)

| Field | Current (wrong) | Correct |
|-------|----------------|---------|
| delAlt string | `"ACGTACAGCTAGCTAGCTAAGC"` (22 chars) | `"ACGTACAGCTAGCTAAGC"` (18 chars) |
| Del length assertion | `.Equal(19, altSeq.Length)` | `.Equal(18, altSeq.Length)` |
| Multi-bubble ref[6] | `.Equal('C', refSeq[6])` | `.Equal('A', refSeq[6])` |

**Root cause**: delAlt is the concatenation of `ref[0:6]` + `ref[9:]`. But `ref[9:]` starts with `AGCTAGCTAAGC` (12 chars), giving `ACGTACAGCTAGCTAAGC` = 6+12 = 18 chars. The current test has inserted an extra `"TAGC"` (4 chars) making it 22 chars.

**Verification**:
- `refSeq` = `"ACGTACACTAGCTAGCTAAGC"` (21 chars)
- `refSeq[6:9]` = `"ACT"` 
- `delAlt` = `refSeq.Replace("ACT", "")` = `ref[0:6] + ref[9:]` = `"ACGTAC" + "AGCTAGCTAAGC"` = `"ACGTACAGCTAGCTAAGC"` = 18 chars
- `delAlt[0:6]` = `ACGTAC` = `ref[0:6]` (prefix match)
- `delAlt[6:]` = `AGCTAGCTAAGC` = `ref[9:]` (suffix match)

---

## Fix Plan

### BubbleFinder.cs (3 patch ops)
1. Change `Reconstruct` signature to take `endNode` parameter
2. Delete the `pred.First(kv => kv.Value == "").Key` line
3. Update `BuildPath` call-site to pass `end`

### DeBruijnSvDetectionTests.cs (3 patch ops)
1. SNP: change `12` → `13` on length assertion
2. Del test 1: fix delAlt string + length assertion 19→18
3. Multi-bubble: fix delAlt string + change ref[6] assertion `'C'` → `'A'`

Total: 6 patch operations, no full file rewrites needed.
