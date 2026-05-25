using System;
using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations.BurrowsWheeler;

/// <summary>
/// Burrows-Wheeler Transform utilities.
///
/// The heavy-lifting suffix array construction is delegated to
/// <see cref="FmIndex.BuildSuffixArray"/> which uses prefix doubling with
/// counting sort (O(n log n)), replacing the original O(n²·log n) suffix-string
/// sort that made this class impractical for references &gt; a few thousand bp.
/// </summary>
public class BurrowsWheelerTransform
{
    // Structure to store data of a rotation
    public struct Rotation
    {
        public int Index;
        public string Suffix;
    }

    // Compares the rotations and sorts them alphabetically
    private static int CompareRotations(Rotation x, Rotation y)
    {
        return string.CompareOrdinal(x.Suffix, y.Suffix);
    }

    // Takes text to be transformed and its length as
    // arguments and returns the corresponding suffix array.
    // NOTE: This naive O(n² log n) implementation is retained for short
    //       strings (< ~10 000 bp).  For longer sequences use
    //       <see cref="FmIndex.BuildSuffixArray"/> directly.
    private static int[] ComputeSuffixArray(string inputText)
    {
        var lenText = inputText.Length;

        var suff = new Rotation[lenText];
        for (var i = 0; i < lenText; i++)
        {
            suff[i].Index = i;
            suff[i].Suffix = inputText[i..];
        }

        Array.Sort(suff, CompareRotations);

        var suffixArr = new int[lenText];
        for (var i = 0; i < lenText; i++)
        {
            suffixArr[i] = suff[i].Index;
        }

        return suffixArr;
    }

    // Takes suffix array and its size as arguments
    // and returns the Burrows-Wheeler Transform of given text
    private static string FindLastChar(string inputText, int[] suffixArr)
    {
        var n = suffixArr.Length;
        var bwtArr = new char[n];
        for (var i = 0; i < n; i++)
        {
            var j = suffixArr[i] - 1;
            if (j < 0)
            {
                j += n;
            }

            bwtArr[i] = inputText[j];
        }

        return new string(bwtArr);
    }

    /// <summary>
    /// Computes the Burrows-Wheeler Transform of <paramref name="inputText"/>
    /// using the O(n log n) prefix-doubling suffix array construction from
    /// <see cref="FmIndex.BuildSuffixArray"/>.
    ///
    /// A sentinel character '$' (value 0) is appended internally.
    /// The returned string has the same length as <paramref name="inputText"/>
    /// plus one (it includes the sentinel row's character).
    /// </summary>
    public static string Transform(string inputText)
    {
        ArgumentNullException.ThrowIfNull(inputText);

        var n = inputText.Length;
        // Encode as bytes; all characters ≥ 1 (sentinel stays 0 at index n)
        var text = new byte[n + 1];
        for (var i = 0; i < n; i++)
        {
            text[i] = (byte)Math.Max(1, (int)(byte)inputText[i]);
        }
        // text[n] = 0  (sentinel, zero-initialised)

        var sa  = FmIndex.BuildSuffixArray(text, n);
        var bwt = new char[n + 1];
        for (var i = 0; i <= n; i++)
        {
            bwt[i] = sa[i] > 0 ? inputText[sa[i] - 1] : '$';
        }

        return new string(bwt);
    }

    public static string Invert(string bwtArr)
    {
        var lenBwt = bwtArr.Length;
        var charArray = bwtArr.ToCharArray();
        Array.Sort(charArray, StringComparer.Ordinal);
        var sortedBwt = new string(charArray);
        var lShift = new int[lenBwt];

        var x = 4;

        var arr = new List<int>[128];
        for (var i = 0; i < arr.Length; i++)
        {
            arr[i] = new List<int>();
        }

        for (var i = 0; i < lenBwt; i++)
        {
            arr[bwtArr[i]].Add(i);
        }

        for (var i = 0; i < lenBwt; i++)
        {
            lShift[i] = arr[sortedBwt[i]][0];
            arr[sortedBwt[i]].RemoveAt(0);
        }

        var decoded = new char[lenBwt];
        for (var i = 0; i < lenBwt; i++)
        {
            x = lShift[x];
            decoded[lenBwt - 1 - i] = bwtArr[x];
        }

        var decodedStr = new string(decoded);
        return decodedStr;
    }
}
