# Needleman-Wunsch Alignment

## Introduction

The Needleman-Wunsch algorithm performs global alignment, meaning every position of both sequences is included in the 
alignment from start to finish. Unlike semi-global alignment (Smith-Waterman), it does not allow gaps at the sequence 
ends without penalty.

This algorithm is designed for protein sequence comparison and uses the BLOSUM62 scoring matrix — a substitution matrix 
derived from observed amino acid replacements in aligned protein families. BLOSUM62 is tuned for detecting homologous 
sequences with moderate divergence, making it ideal for identifying conserved protein domains across-species 
comparisons.

Global alignment is appropriate when comparing full-length sequences (e.g., two complete genes or genes from different 
species) where every position matters and you expect the sequences to be roughly the same length. In contrast, 
short-read sequencing alignment uses Smith-Waterman semi-global alignment because reads are fragments that only cover 
part of the reference genome.

```csharp
using OpenMedStack.BioSharp.Calculations.NeedlemanWunsch;

// Load BLOSUM62 (built-in) and align protein sequences
// The Align method returns an AlignmentResult compatible with the same interface
```

This method provides a different alignment paradigm that can be used when global alignment is the appropriate choice, 
complementing the semi-global Smith-Waterman approach used for read mapping.
