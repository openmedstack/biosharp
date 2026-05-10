using System.Collections.ObjectModel;

namespace OpenMedStack.BioSharp.Calculations.NeedlemanWunsch;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Model;

public static class NeedlemanWunschAlgo
{
    private class MemStringEqualityComparer(bool ignoreCase = false) :
        IEqualityComparer<ReadOnlyMemory<char>>
    {
        private readonly StringComparison _comparison = ignoreCase
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        public int GetHashCode(ReadOnlyMemory<char> obj)
        {
            return string.GetHashCode(obj.Span, _comparison);
        }

        public bool Equals(
            ReadOnlyMemory<char> x,
            ReadOnlyMemory<char> y)
        {
            return x.Span.Equals(y.Span, _comparison);
        }
    }

    private static readonly ReadOnlyDictionary<ReadOnlyMemory<char>, sbyte> Blossum62 =
        new(new Dictionary<ReadOnlyMemory<char>, sbyte>(
            new Dictionary<ReadOnlyMemory<char>, sbyte>
            {
                #region MyRegion

                /*
                A Ala  4
                R Arg -1   5
                N Asn -2   0   6
                D Asp -2  -2   1   6
                C Cys  0  -3  -3  -3   9
                Q Gln -1   1   0   0  -3   5
                E Glu -1   0   0   2  -4   2   5
                G Gly  0  -2   0  -1  -3  -2  -2   6
                H His -2   0   1  -1  -3   0   0  -2   8
                I Ile -1  -3  -3  -3  -1  -3  -3  -4  -3   4
                L Leu -1  -2  -3  -4  -1  -2  -3  -4  -3   2   4
                K Lys -1   2   0  -1  -3   1   1  -2  -1  -3  -2   5
                M Met -1  -1  -2  -3  -1   0  -2  -3  -2   1   2  -1   5
                F Phe -2  -3  -3  -3  -2  -3  -3  -3  -1   0   0  -3   0   6
                P Pro -1  -2  -2  -1  -3  -1  -1  -2  -2  -3  -3  -1  -2  -4   7
                S Ser  1  -1   1   0  -1   0   0   0  -1  -2  -2   0  -1  -2  -1   4
                T Thr  0  -1   0  -1  -1  -1  -1  -2  -2  -1  -1  -1  -1  -2  -1   1   5
                W Trp -3  -3  -4  -4  -2  -2  -3  -2  -2  -3  -2  -3  -1   1  -4  -3  -2  11
                Y Tyr -2  -2  -2  -3  -2  -1  -2  -3   2  -1  -1  -2  -1   3  -3  -2  -2   2   7
                V Val  0  -3  -3  -3  -1  -2  -2  -3  -3   3   1  -2   1  -1  -2  -2   0  -3  -1  4
                     Ala Arg Asn Asp Cys Gln Glu Gly His Ile Leu Lys Met Phe Pro Ser Thr Trp Tyr Val
                      A   R   N   D   C   Q   E   G   H   I   L   K   M   F   P   S   T   W   Y   V
                 */

                ["AA".AsMemory()] = 4,
                ["AR".AsMemory()] = -1,
                ["AN".AsMemory()] = -2,
                ["AD".AsMemory()] = -2,
                ["AC".AsMemory()] = 0,
                ["AQ".AsMemory()] = -1,
                ["AE".AsMemory()] = -1,
                ["AG".AsMemory()] = 0,
                ["AH".AsMemory()] = -2,
                ["AI".AsMemory()] = -1,
                ["AL".AsMemory()] = -1,
                ["AK".AsMemory()] = -1,
                ["AM".AsMemory()] = -1,
                ["AF".AsMemory()] = -2,
                ["AP".AsMemory()] = -1,
                ["AS".AsMemory()] = 1,
                ["AT".AsMemory()] = 0,
                ["AW".AsMemory()] = -3,
                ["AY".AsMemory()] = -2,
                ["AV".AsMemory()] = 0,
                ["RA".AsMemory()] = -1,
                ["RR".AsMemory()] = 5,
                ["RN".AsMemory()] = 0,
                ["RD".AsMemory()] = -2,
                ["RC".AsMemory()] = -3,
                ["RQ".AsMemory()] = 1,
                ["RE".AsMemory()] = 0,
                ["RG".AsMemory()] = -2,
                ["RH".AsMemory()] = 0,
                ["RI".AsMemory()] = -3,
                ["RL".AsMemory()] = -2,
                ["RK".AsMemory()] = 2,
                ["RM".AsMemory()] = -1,
                ["RF".AsMemory()] = -3,
                ["RP".AsMemory()] = -2,
                ["RS".AsMemory()] = -1,
                ["RT".AsMemory()] = -1,
                ["RW".AsMemory()] = -3,
                ["RY".AsMemory()] = -2,
                ["RV".AsMemory()] = -3,
                ["NA".AsMemory()] = -2,
                ["NR".AsMemory()] = 0,
                ["NN".AsMemory()] = 6,
                ["ND".AsMemory()] = 1,
                ["NC".AsMemory()] = -3,
                ["NQ".AsMemory()] = 0,
                ["NE".AsMemory()] = 0,
                ["NG".AsMemory()] = 0,
                ["NH".AsMemory()] = 1,
                ["NI".AsMemory()] = -3,
                ["NL".AsMemory()] = -3,
                ["NK".AsMemory()] = 0,
                ["NM".AsMemory()] = -2,
                ["NF".AsMemory()] = -3,
                ["NP".AsMemory()] = -2,
                ["NS".AsMemory()] = 1,
                ["NT".AsMemory()] = 0,
                ["NW".AsMemory()] = -4,
                ["NY".AsMemory()] = -2,
                ["NV".AsMemory()] = -3,
                ["DA".AsMemory()] = -2,
                ["DR".AsMemory()] = -2,
                ["DN".AsMemory()] = 1,
                ["DD".AsMemory()] = 6,
                ["DC".AsMemory()] = -3,
                ["DQ".AsMemory()] = 0,
                ["DE".AsMemory()] = 2,
                ["DG".AsMemory()] = -1,
                ["DH".AsMemory()] = -1,
                ["DI".AsMemory()] = -3,
                ["DL".AsMemory()] = -4,
                ["DK".AsMemory()] = -1,
                ["DM".AsMemory()] = -3,
                ["DF".AsMemory()] = -3,
                ["DP".AsMemory()] = -1,
                ["DS".AsMemory()] = 0,
                ["DT".AsMemory()] = -1,
                ["DW".AsMemory()] = -4,
                ["DY".AsMemory()] = -3,
                ["DV".AsMemory()] = -3,
                ["CA".AsMemory()] = 0,
                ["CR".AsMemory()] = -3,
                ["CN".AsMemory()] = -3,
                ["CD".AsMemory()] = -3,
                ["CC".AsMemory()] = 9,
                ["CQ".AsMemory()] = -3,
                ["CE".AsMemory()] = -3,
                ["CG".AsMemory()] = -3,
                ["CH".AsMemory()] = -3,
                ["CI".AsMemory()] = -1,
                ["CL".AsMemory()] = -1,
                ["CK".AsMemory()] = -3,
                ["CM".AsMemory()] = -1,
                ["CF".AsMemory()] = -2,
                ["CP".AsMemory()] = -3,
                ["CS".AsMemory()] = -1,
                ["CT".AsMemory()] = -1,
                ["CW".AsMemory()] = -2,
                ["CY".AsMemory()] = -2,
                ["CV".AsMemory()] = -1,
                ["QA".AsMemory()] = -1,
                ["QR".AsMemory()] = 1,
                ["QN".AsMemory()] = 0,
                ["QD".AsMemory()] = 0,
                ["QC".AsMemory()] = -3,
                ["QQ".AsMemory()] = 5,
                ["QE".AsMemory()] = 2,
                ["QG".AsMemory()] = -2,
                ["QH".AsMemory()] = 0,
                ["QI".AsMemory()] = -3,
                ["QL".AsMemory()] = -2,
                ["QK".AsMemory()] = 1,
                ["QM".AsMemory()] = 0,
                ["QF".AsMemory()] = -3,
                ["QP".AsMemory()] = -1,
                ["QS".AsMemory()] = 0,
                ["QT".AsMemory()] = -1,
                ["QW".AsMemory()] = -2,
                ["QY".AsMemory()] = -1,
                ["QV".AsMemory()] = -2,
                ["EA".AsMemory()] = -1,
                ["ER".AsMemory()] = 0,
                ["EN".AsMemory()] = 0,
                ["ED".AsMemory()] = 2,
                ["EC".AsMemory()] = -4,
                ["EQ".AsMemory()] = 2,
                ["EE".AsMemory()] = 5,
                ["EG".AsMemory()] = -2,
                ["EH".AsMemory()] = 0,
                ["EI".AsMemory()] = -3,
                ["EL".AsMemory()] = -3,
                ["EK".AsMemory()] = 1,
                ["EM".AsMemory()] = -2,
                ["EF".AsMemory()] = -3,
                ["EP".AsMemory()] = -1,
                ["ES".AsMemory()] = 0,
                ["ET".AsMemory()] = -1,
                ["EW".AsMemory()] = -3,
                ["EY".AsMemory()] = -2,
                ["EV".AsMemory()] = -2,
                ["GA".AsMemory()] = 0,
                ["GR".AsMemory()] = -2,
                ["GN".AsMemory()] = 0,
                ["GD".AsMemory()] = -1,
                ["GC".AsMemory()] = -3,
                ["GQ".AsMemory()] = -2,
                ["GE".AsMemory()] = -2,
                ["GG".AsMemory()] = 6,
                ["GH".AsMemory()] = -2,
                ["GI".AsMemory()] = -4,
                ["GL".AsMemory()] = -4,
                ["GK".AsMemory()] = -2,
                ["GM".AsMemory()] = -3,
                ["GF".AsMemory()] = -3,
                ["GP".AsMemory()] = -2,
                ["GS".AsMemory()] = 0,
                ["GT".AsMemory()] = -2,
                ["GW".AsMemory()] = -2,
                ["GY".AsMemory()] = -3,
                ["GV".AsMemory()] = -3,
                ["HA".AsMemory()] = -2,
                ["HR".AsMemory()] = 0,
                ["HN".AsMemory()] = 1,
                ["HD".AsMemory()] = -1,
                ["HC".AsMemory()] = -3,
                ["HQ".AsMemory()] = 0,
                ["HE".AsMemory()] = 0,
                ["HG".AsMemory()] = -2,
                ["HH".AsMemory()] = 8,
                ["HI".AsMemory()] = -3,
                ["HL".AsMemory()] = -3,
                ["HK".AsMemory()] = -1,
                ["HM".AsMemory()] = -2,
                ["HF".AsMemory()] = -1,
                ["HP".AsMemory()] = -2,
                ["HS".AsMemory()] = -1,
                ["HT".AsMemory()] = -2,
                ["HW".AsMemory()] = -2,
                ["HY".AsMemory()] = 2,
                ["HV".AsMemory()] = -3,
                ["IA".AsMemory()] = -1,
                ["IR".AsMemory()] = -3,
                ["IN".AsMemory()] = -3,
                ["ID".AsMemory()] = -3,
                ["IC".AsMemory()] = -1,
                ["IQ".AsMemory()] = -3,
                ["IE".AsMemory()] = -3,
                ["IG".AsMemory()] = -4,
                ["IH".AsMemory()] = -3,
                ["II".AsMemory()] = 4,
                ["IL".AsMemory()] = 2,
                ["IK".AsMemory()] = -3,
                ["IM".AsMemory()] = 1,
                ["IF".AsMemory()] = 0,
                ["IP".AsMemory()] = -3,
                ["IS".AsMemory()] = -2,
                ["IT".AsMemory()] = -1,
                ["IW".AsMemory()] = -3,
                ["IY".AsMemory()] = -1,
                ["IV".AsMemory()] = 3,
                ["LA".AsMemory()] = -1,
                ["LR".AsMemory()] = -2,
                ["LN".AsMemory()] = -3,
                ["LD".AsMemory()] = -4,
                ["LC".AsMemory()] = -1,
                ["LQ".AsMemory()] = -2,
                ["LE".AsMemory()] = -3,
                ["LG".AsMemory()] = -4,
                ["LH".AsMemory()] = -3,
                ["LI".AsMemory()] = 2,
                ["LL".AsMemory()] = 4,
                ["LK".AsMemory()] = -2,
                ["LM".AsMemory()] = 2,
                ["LF".AsMemory()] = 0,
                ["LP".AsMemory()] = -3,
                ["LS".AsMemory()] = -2,
                ["LT".AsMemory()] = -1,
                ["LW".AsMemory()] = -2,
                ["LY".AsMemory()] = -1,
                ["LV".AsMemory()] = 1,
                ["KA".AsMemory()] = -1,
                ["KR".AsMemory()] = 2,
                ["KN".AsMemory()] = 0,
                ["KD".AsMemory()] = -1,
                ["KC".AsMemory()] = -3,
                ["KQ".AsMemory()] = 1,
                ["KE".AsMemory()] = 1,
                ["KG".AsMemory()] = -2,
                ["KH".AsMemory()] = -1,
                ["KI".AsMemory()] = -3,
                ["KL".AsMemory()] = -2,
                ["KK".AsMemory()] = 5,
                ["KM".AsMemory()] = -1,
                ["KF".AsMemory()] = -3,
                ["KP".AsMemory()] = -1,
                ["KS".AsMemory()] = 0,
                ["KT".AsMemory()] = -1,
                ["KW".AsMemory()] = -3,
                ["KY".AsMemory()] = -2,
                ["KV".AsMemory()] = -2,
                ["MA".AsMemory()] = -1,
                ["MR".AsMemory()] = -1,
                ["MN".AsMemory()] = -2,
                ["MD".AsMemory()] = -3,
                ["MC".AsMemory()] = -1,
                ["MQ".AsMemory()] = 0,
                ["ME".AsMemory()] = -2,
                ["MG".AsMemory()] = -3,
                ["MH".AsMemory()] = -2,
                ["MI".AsMemory()] = 1,
                ["ML".AsMemory()] = 2,
                ["MK".AsMemory()] = -1,
                ["MM".AsMemory()] = 5,
                ["MF".AsMemory()] = 0,
                ["MP".AsMemory()] = -2,
                ["MS".AsMemory()] = -1,
                ["MT".AsMemory()] = -1,
                ["MW".AsMemory()] = -1,
                ["MY".AsMemory()] = -1,
                ["MV".AsMemory()] = 1,
                ["FA".AsMemory()] = -2,
                ["FR".AsMemory()] = -3,
                ["FN".AsMemory()] = -3,
                ["FD".AsMemory()] = -3,
                ["FC".AsMemory()] = -2,
                ["FQ".AsMemory()] = -3,
                ["FE".AsMemory()] = -3,
                ["FG".AsMemory()] = -3,
                ["FH".AsMemory()] = -1,
                ["FI".AsMemory()] = 0,
                ["FL".AsMemory()] = 0,
                ["FK".AsMemory()] = -3,
                ["FM".AsMemory()] = 0,
                ["FF".AsMemory()] = 6,
                ["FP".AsMemory()] = -4,
                ["FS".AsMemory()] = -2,
                ["FT".AsMemory()] = -2,
                ["FW".AsMemory()] = 1,
                ["FY".AsMemory()] = 3,
                ["FV".AsMemory()] = -1,
                ["PA".AsMemory()] = -1,
                ["PR".AsMemory()] = -2,
                ["PN".AsMemory()] = -2,
                ["PD".AsMemory()] = -1,
                ["PC".AsMemory()] = -3,
                ["PQ".AsMemory()] = -1,
                ["PE".AsMemory()] = -1,
                ["PG".AsMemory()] = -2,
                ["PH".AsMemory()] = -2,
                ["PI".AsMemory()] = -3,
                ["PL".AsMemory()] = -3,
                ["PK".AsMemory()] = -1,
                ["PM".AsMemory()] = -2,
                ["PF".AsMemory()] = -4,
                ["PP".AsMemory()] = 7,
                ["PS".AsMemory()] = -1,
                ["PT".AsMemory()] = -1,
                ["PW".AsMemory()] = -4,
                ["PY".AsMemory()] = -3,
                ["PV".AsMemory()] = -2,
                ["SA".AsMemory()] = 1,
                ["SR".AsMemory()] = -1,
                ["SN".AsMemory()] = 1,
                ["SD".AsMemory()] = 0,
                ["SC".AsMemory()] = -1,
                ["SQ".AsMemory()] = 0,
                ["SE".AsMemory()] = 0,
                ["SG".AsMemory()] = 0,
                ["SH".AsMemory()] = -1,
                ["SI".AsMemory()] = -2,
                ["SL".AsMemory()] = -2,
                ["SK".AsMemory()] = 0,
                ["SM".AsMemory()] = -1,
                ["SF".AsMemory()] = -2,
                ["SP".AsMemory()] = -1,
                ["SS".AsMemory()] = 4,
                ["ST".AsMemory()] = 1,
                ["SW".AsMemory()] = -3,
                ["SY".AsMemory()] = -2,
                ["SV".AsMemory()] = -2,
                ["TA".AsMemory()] = 0,
                ["TR".AsMemory()] = -1,
                ["TN".AsMemory()] = 0,
                ["TD".AsMemory()] = -1,
                ["TC".AsMemory()] = -1,
                ["TQ".AsMemory()] = -1,
                ["TE".AsMemory()] = -1,
                ["TG".AsMemory()] = -2,
                ["TH".AsMemory()] = -2,
                ["TI".AsMemory()] = -1,
                ["TL".AsMemory()] = -1,
                ["TK".AsMemory()] = -1,
                ["TM".AsMemory()] = -1,
                ["TF".AsMemory()] = -2,
                ["TP".AsMemory()] = -1,
                ["TS".AsMemory()] = 1,
                ["TT".AsMemory()] = 5,
                ["TW".AsMemory()] = -2,
                ["TY".AsMemory()] = -2,
                ["TV".AsMemory()] = 0,
                ["WA".AsMemory()] = -3,
                ["WR".AsMemory()] = -3,
                ["WN".AsMemory()] = -4,
                ["WD".AsMemory()] = -4,
                ["WC".AsMemory()] = -2,
                ["WQ".AsMemory()] = -2,
                ["WE".AsMemory()] = -3,
                ["WG".AsMemory()] = -2,
                ["WH".AsMemory()] = -2,
                ["WI".AsMemory()] = -3,
                ["WL".AsMemory()] = -2,
                ["WK".AsMemory()] = -3,
                ["WM".AsMemory()] = -1,
                ["WF".AsMemory()] = 1,
                ["WP".AsMemory()] = -4,
                ["WS".AsMemory()] = -3,
                ["WT".AsMemory()] = -2,
                ["WW".AsMemory()] = 11,
                ["WY".AsMemory()] = 2,
                ["WV".AsMemory()] = -3,
                ["YA".AsMemory()] = -2,
                ["YR".AsMemory()] = -2,
                ["YN".AsMemory()] = -2,
                ["YD".AsMemory()] = -3,
                ["YC".AsMemory()] = -2,
                ["YQ".AsMemory()] = -1,
                ["YE".AsMemory()] = -2,
                ["YG".AsMemory()] = -3,
                ["YH".AsMemory()] = 2,
                ["YI".AsMemory()] = -1,
                ["YK".AsMemory()] = -2,
                ["YM".AsMemory()] = -1,
                ["YF".AsMemory()] = 3,
                ["YP".AsMemory()] = -3,
                ["YS".AsMemory()] = -2,
                ["YT".AsMemory()] = -2,
                ["YW".AsMemory()] = 2,
                ["YY".AsMemory()] = 7,
                ["YV".AsMemory()] = -1,
                ["VA".AsMemory()] = 0,
                ["VR".AsMemory()] = -3,
                ["VN".AsMemory()] = -3,
                ["VD".AsMemory()] = -3,
                ["VC".AsMemory()] = -1,
                ["VQ".AsMemory()] = -2,
                ["VE".AsMemory()] = -2,
                ["VG".AsMemory()] = -3,
                ["VH".AsMemory()] = -3,
                ["VI".AsMemory()] = 3,
                ["VL".AsMemory()] = 1,
                ["VK".AsMemory()] = -2,
                ["VM".AsMemory()] = 1,
                ["VF".AsMemory()] = -1,
                ["VP".AsMemory()] = -2,
                ["VS".AsMemory()] = -2,
                ["VT".AsMemory()] = 0,
                ["VW".AsMemory()] = -3,
                ["VY".AsMemory()] = -1,
                ["VV".AsMemory()] = 4

                #endregion
            }, new MemStringEqualityComparer()));

    private static Direction[,] InitializeMatrix(
        ReadOnlySpan<char> reference,
        ReadOnlySpan<char> left,
        byte penalty)
    {
        var scoreData = new int[left.Length + 1][];
        for (var i = 0; i <= left.Length; i++)
        {
            scoreData[i] = new int[reference.Length + 1];
        }

        var traverseData = new Direction[left.Length + 1, reference.Length + 1];
        scoreData[0][0] = 0;
        for (var j = 1; j <= left.Length; j++)
        {
            scoreData[j][0] = j * -penalty;
            traverseData[j, 0] = Direction.Up;
        }

        for (var i = 1; i <= reference.Length; i++)
        {
            scoreData[0][i] = i * -penalty;
            traverseData[0, i] = Direction.Left;
        }

        for (var i = 1; i <= reference.Length; i++)
        {
            for (var j = 1; j <= left.Length; j++)
            {
                var options = new (int, Direction)[3];
                var i2 = i - 1;
                var j2 = j - 1;

                if (!Blossum62.TryGetValue(new ReadOnlyMemory<char>([reference[i2], left[j2]]),
                    out var blosumValue))
                {
                    Blossum62.TryGetValue(
                        new ReadOnlyMemory<char>([char.ToUpper(reference[i2]), char.ToUpper(left[j2])]),
                        out blosumValue);
                }

                options[0] = (
                    scoreData[j2][i2] + blosumValue,
                    Direction.Diagonal);
                options[1] = (scoreData[j2][i] - penalty, Direction.Up);
                options[2] = (scoreData[j][i2] - penalty, Direction.Left);
                var (score, direction) = options.OrderByDescending(x => x.Item1).First();
                scoreData[j][i] = score;
                traverseData[j, i] = direction;
            }
        }

        return traverseData;
    }

    public static Task<(int index, ReadOnlyMemory<char> alignment)> Align(
        this Sequence reference,
        Sequence second,
        byte penalty)
    {
        return reference.GetData().Align(second.GetData(),
            penalty);
    }

    public static async Task<(int index, ReadOnlyMemory<char> alignment)> Align(
        this ReadOnlyMemory<char> reference,
        ReadOnlyMemory<char> second,
        byte penalty)
    {
        await Task.Yield();

        var matrixData = InitializeMatrix(reference.Span, second.Span, penalty);
        var leftStack = new Stack<char>(second.Length);
        var j = matrixData.GetLength(0) - 1;
        var i = matrixData.GetLength(1) - 1;
        while (i > 0 || j > 0)
        {
            var direction = matrixData[j, i];

            switch (direction)
            {
                case Direction.Diagonal:
                    leftStack.Push(second.Span[j - 1]);
                    i -= 1;
                    j -= 1;
                    break;
                case Direction.Left:
                    leftStack.Push('-');
                    i -= 1;
                    break;
                case Direction.Up:
                    leftStack.Push(second.Span[j - 1]);
                    j -= 1;
                    break;
                case Direction.Done:
                    break;
                default:
                    throw new InvalidOperationException("Invalid direction");
            }
        }

        return (i, new ReadOnlyMemory<char>(leftStack.ToArray()));
    }
}
