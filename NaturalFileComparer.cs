using System;
using System.Collections.Generic;

namespace opti_pdf
{
    /// <summary>
    /// Orders paths the way a person reads them: digit runs compare as numbers,
    /// so page2 sorts before page10 instead of after it.
    ///
    /// Compares the path string directly rather than splitting it into directory
    /// and file parts. Splitting would allocate four strings per comparison and
    /// call Path APIs that can throw, and a comparer that throws inside
    /// List&lt;T&gt;.Sort takes down the whole batch with an unhelpful
    /// "Failed to compare two elements in the array."
    /// </summary>
    internal sealed class NaturalFileComparer : IComparer<string>
    {
        public static readonly NaturalFileComparer Instance = new NaturalFileComparer();

        private NaturalFileComparer()
        {
        }

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int i = 0, j = 0;

            while (i < x.Length && j < y.Length)
            {
                var digitX = IsDigit(x[i]);
                var digitY = IsDigit(y[j]);

                if (digitX && digitY)
                {
                    var result = CompareDigitRuns(x, ref i, y, ref j);
                    if (result != 0)
                        return result;

                    continue;
                }

                if (digitX != digitY)
                    return digitX ? -1 : 1;

                var cx = char.ToUpperInvariant(x[i]);
                var cy = char.ToUpperInvariant(y[j]);
                if (cx != cy)
                    return cx < cy ? -1 : 1;

                i++;
                j++;
            }

            var remaining = (x.Length - i).CompareTo(y.Length - j);

            // Equal ignoring case and numbering: fall back to ordinal so the
            // order is total and stable rather than dependent on input order.
            return remaining != 0 ? remaining : string.CompareOrdinal(x, y);
        }

        /// <summary>
        /// Compares two digit runs numerically without parsing, so a run longer
        /// than any integer type still compares correctly.
        /// </summary>
        private static int CompareDigitRuns(string x, ref int i, string y, ref int j)
        {
            // Leading zeros carry no value, but are remembered as a tiebreak so
            // "007" and "7" order deterministically.
            int startX = i;
            while (startX < x.Length && x[startX] == '0')
                startX++;

            int startY = j;
            while (startY < y.Length && y[startY] == '0')
                startY++;

            int endX = startX;
            while (endX < x.Length && IsDigit(x[endX]))
                endX++;

            int endY = startY;
            while (endY < y.Length && IsDigit(y[endY]))
                endY++;

            var lengthX = endX - startX;
            var lengthY = endY - startY;

            // With leading zeros gone, the longer run is the larger number.
            if (lengthX != lengthY)
                return lengthX < lengthY ? -1 : 1;

            for (var k = 0; k < lengthX; k++)
            {
                if (x[startX + k] != y[startY + k])
                    return x[startX + k] < y[startY + k] ? -1 : 1;
            }

            var zerosX = startX - i;
            var zerosY = startY - j;

            i = endX;
            j = endY;

            return zerosX == zerosY ? 0 : (zerosX < zerosY ? -1 : 1);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
