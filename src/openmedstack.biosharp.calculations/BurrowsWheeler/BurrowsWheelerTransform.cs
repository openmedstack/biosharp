using System;
using System.Collections.Generic;

namespace OpenMedStack.BioSharp.Calculations.BurrowsWheeler;

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
    // arguments and returns the corresponding suffix array
    private static int[] ComputeSuffixArray(string inputText)
    {
        var lenText = inputText.Length;

        // Array of structures to store rotations and their indexes
        var suff = new Rotation[lenText];

        // Structure is needed to maintain old indexes of rotations
        // after sorting them
        for (var i = 0; i < lenText; i++)
        {
            suff[i].Index = i;
            suff[i].Suffix = inputText.Substring(i);
        }

        // Sorts rotations using comparison function defined above
        Array.Sort(suff, CompareRotations);

        // Stores the indexes of sorted rotations
        var suffixArr = new int[lenText];
        for (var i = 0; i < lenText; i++)
        {
            suffixArr[i] = suff[i].Index;
        }

        // Returns the computed suffix array
        return suffixArr;
    }

    // Takes suffix array and its size as arguments
    // and returns the Burrows-Wheeler Transform of given text
    private static string FindLastChar(string inputText, int[] suffixArr)
    {
        var n = suffixArr.Length;

        // Iterates over the suffix array to find the last char of each cyclic rotation
        var bwtArr = new char[n];
        for (var i = 0; i < n; i++)
        {
            // Computes the last char which is given by inputText[(suffixArr[i] + n - 1) % n]
            var j = suffixArr[i] - 1;
            if (j < 0)
            {
                j += n;
            }

            bwtArr[i] = inputText[j];
        }

        // Returns the computed Burrows-Wheeler Transform
        return new string(bwtArr);
    }

    public static string Invert(string bwtArr) {
        var lenBwt = bwtArr.Length;
        var charArray = bwtArr.ToCharArray();
        Array.Sort(charArray, StringComparer.Ordinal);
        var sortedBwt = new string(charArray);
        var lShift = new int[lenBwt];

        // Index at which original string appears
        // in the sorted rotations list
        var x = 4;

        // Array of lists to compute l_shift
        var arr = new List<int>[128];
        for (var i = 0; i < arr.Length; i++) {
            arr[i] = new List<int>();
        }

        // Adds each character of bwtArr to a linked list
        // and appends to it the new node whose data part
        // contains index at which character occurs in bwtArr
        for (var i = 0; i < lenBwt; i++) {
            arr[bwtArr[i]].Add(i);
        }

        // Adds each character of sortedBwt to a linked list
        // and finds lShift
        for (var i = 0; i < lenBwt; i++) {
            lShift[i] = arr[sortedBwt[i]][0];
            arr[sortedBwt[i]].RemoveAt(0);
        }

        // Decodes the bwt
        var decoded = new char[lenBwt];
        for (var i = 0; i < lenBwt; i++) {
            x = lShift[x];
            decoded[lenBwt-1-i] = bwtArr[x];
        }
        var decodedStr = new string(decoded);
        return decodedStr;
    }
}
