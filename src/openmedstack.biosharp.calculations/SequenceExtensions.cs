namespace OpenMedStack.BioSharp.Calculations
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Model;

    public static class SequenceExtensions
    {
        private static readonly string[] StopCodons = { "UAA", "UAG", "UGA" };

        public static int IndexOf(this Sequence sequence, char[] segment, int maxErrors = 0, int startIndex = 0)
        {
            var currentErrors = 0;
            if (startIndex > sequence.Length)
            {
                return -1;
            }

            for (var i = startIndex; i < sequence.Length - segment.Length; i++)
            {
                var contains = true;
                for (var j = 0; j < segment.Length; j++)
                {
                    if (sequence[i + j] == segment[j])
                    {
                        continue;
                    }

                    currentErrors++;
                    if (currentErrors <= maxErrors)
                    {
                        continue;
                    }

                    contains = false;
                    break;
                }

                if (contains)
                {
                    return i;
                }

                currentErrors = 0;
            }

            return -1;
        }

        public static IEnumerable<int> AllIndicesOf(this Sequence sequence, char[] segment, int maxErrors = 0, int startIndex = 0, bool nonOverlapping = false)
        {
            var start = startIndex;
            while (true)
            {
                var index = IndexOf(sequence, segment, maxErrors, start);
                if (index == -1)
                {
                    yield break;
                }

                start = index + (nonOverlapping ? segment.Length : 1);
                yield return index;
            }
        }

        public static int Count(this Sequence sequence, char[] segment, int maxErrors = 0, int startIndex = 0, bool nonOverlapping = false)
        {
            var start = startIndex;
            var count = 0;
            while (true)
            {
                var index = IndexOf(sequence, segment, maxErrors, start);
                if (index == -1)
                {
                    return count;
                }

                count++;
                start = index + (nonOverlapping ? segment.Length : 1);
            }
        }

        public static Sequence ToRna(this Sequence sequence)
        {
            var complemented = new char[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
            {
                complemented[i] = sequence[i] switch
                {
                    'T' => (char)85,
                    var c => c
                };
            }
            return new Sequence(sequence.Id, complemented, new char[sequence.Length]);
        }

        public static Sequence ComplementDna(this Sequence sequence)
        {
            var complemented = new char[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
            {
                var c = sequence[i];
                complemented[i] = c switch
                {
                    'A' => (char)84,
                    'T' => (char)65,
                    'C' => (char)71,
                    'G' => (char)67,
                    'U' => (char)84,
                    _ => throw new ArgumentOutOfRangeException(nameof(c), "Invalid character")
                };
            }
            return new Sequence(sequence.Id, complemented, new char[sequence.Length]);
        }

        public static Sequence ComplementRna(this Sequence sequence)
        {
            var complemented = new char[sequence.Length];
            for (var i = 0; i < sequence.Length; i++)
            {
                var c = sequence[i];
                complemented[i] = c switch
                {
                    'A' => (char)85,
                    'C' => (char)71,
                    'G' => (char)67,
                    'U' => (char)65,
                    _ => throw new ArgumentOutOfRangeException(nameof(c), "Invalid character")
                };
            }
            return new Sequence(sequence.Id, complemented, new char[sequence.Length]);
        }

        public static IEnumerable<AminoAcid> ReadAminoAcids(this Sequence sequence, bool toStop = false)
        {
            for (var i = 0; i < sequence.Length; i += 3)
            {
                var codon = new string(new[] { sequence[i], sequence[i + 1], sequence[i + 2] });
                if (StopCodons.Contains(codon) && toStop)
                {
                    yield break;
                }

                yield return codon switch
                {
                    "UUU" => 'F',
                    "UUC" => 'F',
                    "UUA" => 'L',
                    "UUG" => 'L',
                    "UCU" => 'S',
                    "UCC" => 'S',
                    "UCA" => 'S',
                    "UCG" => 'S',
                    "UAU" => 'Y',
                    "UAC" => 'Y',
                    "UAA" => '*',
                    "UAG" => '*',
                    "UGU" => 'C',
                    "UGC" => 'C',
                    "UGA" => '*',
                    "UGG" => 'W',
                    "CUU" => 'L',
                    "CUC" => 'L',
                    "CUA" => 'L',
                    "CUG" => 'L',
                    "CCU" => 'P',
                    "CCC" => 'P',
                    "CCA" => 'P',
                    "CCG" => 'P',
                    "CAU" => 'H',
                    "CAC" => 'H',
                    "CAA" => 'Q',
                    "CAG" => 'Q',
                    "CGU" => 'R',
                    "CGC" => 'R',
                    "CGA" => 'R',
                    "CGG" => 'R',
                    "AUU" => 'I',
                    "AUC" => 'I',
                    "AUA" => 'I',
                    "AUG" => 'M',
                    "ACU" => 'T',
                    "ACC" => 'T',
                    "ACA" => 'T',
                    "ACG" => 'T',
                    "AAU" => 'N',
                    "AAC" => 'N',
                    "AAA" => 'K',
                    "AAG" => 'K',
                    "AGU" => 'S',
                    "AGC" => 'S',
                    "AGA" => 'R',
                    "AGG" => 'R',
                    "GUU" => 'V',
                    "GUC" => 'V',
                    "GUA" => 'V',
                    "GUG" => 'V',
                    "GCU" => 'A',
                    "GCC" => 'A',
                    "GCA" => 'A',
                    "GCG" => 'A',
                    "GAU" => 'D',
                    "GAC" => 'D',
                    "GAA" => 'E',
                    "GAG" => 'E',
                    "GGU" => 'G',
                    "GGC" => 'G',
                    "GGA" => 'G',
                    "GGG" => 'G',
                    _ => throw new ArgumentOutOfRangeException(nameof(codon), "Invalid codon")
                };
            }
        }
    }
}
