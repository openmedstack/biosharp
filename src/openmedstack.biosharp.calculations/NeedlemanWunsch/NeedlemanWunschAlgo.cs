namespace OpenMedStack.BioSharp.Calculations.NeedlemanWunsch
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using Model;

    public static class NeedlemanWunschAlgo
    {
        private static readonly Dictionary<string, sbyte> Blossum62 = new()
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

            ["AA"] = 4,
            ["AR"] = -1,
            ["AN"] = -2,
            ["AD"] = -2,
            ["AC"] = 0,
            ["AQ"] = -1,
            ["AE"] = -1,
            ["AG"] = 0,
            ["AH"] = -2,
            ["AI"] = -1,
            ["AL"] = -1,
            ["AK"] = -1,
            ["AM"] = -1,
            ["AF"] = -2,
            ["AP"] = -1,
            ["AS"] = 1,
            ["AT"] = 0,
            ["AW"] = -3,
            ["AY"] = -2,
            ["AV"] = 0,
            ["RA"] = -1,
            ["RR"] = 5,
            ["RN"] = 0,
            ["RD"] = -2,
            ["RC"] = -3,
            ["RQ"] = 1,
            ["RE"] = 0,
            ["RG"] = -2,
            ["RH"] = 0,
            ["RI"] = -3,
            ["RL"] = -2,
            ["RK"] = 2,
            ["RM"] = -1,
            ["RF"] = -3,
            ["RP"] = -2,
            ["RS"] = -1,
            ["RT"] = -1,
            ["RW"] = -3,
            ["RY"] = -2,
            ["RV"] = -3,
            ["NA"] = -2,
            ["NR"] = 0,
            ["NN"] = 6,
            ["ND"] = 1,
            ["NC"] = -3,
            ["NQ"] = 0,
            ["NE"] = 0,
            ["NG"] = 0,
            ["NH"] = 1,
            ["NI"] = -3,
            ["NL"] = -3,
            ["NK"] = 0,
            ["NM"] = -2,
            ["NF"] = -3,
            ["NP"] = -2,
            ["NS"] = 1,
            ["NT"] = 0,
            ["NW"] = -4,
            ["NY"] = -2,
            ["NV"] = -3,
            ["DA"] = -2,
            ["DR"] = -2,
            ["DN"] = 1,
            ["DD"] = 6,
            ["DC"] = -3,
            ["DQ"] = 0,
            ["DE"] = 2,
            ["DG"] = -1,
            ["DH"] = -1,
            ["DI"] = -3,
            ["DL"] = -4,
            ["DK"] = -1,
            ["DM"] = -3,
            ["DF"] = -3,
            ["DP"] = -1,
            ["DS"] = 0,
            ["DT"] = -1,
            ["DW"] = -4,
            ["DY"] = -3,
            ["DV"] = -3,
            ["CA"] = 0,
            ["CR"] = -3,
            ["CN"] = -3,
            ["CD"] = -3,
            ["CC"] = 9,
            ["CQ"] = -3,
            ["CE"] = -3,
            ["CG"] = -3,
            ["CH"] = -3,
            ["CI"] = -1,
            ["CL"] = -1,
            ["CK"] = -3,
            ["CM"] = -1,
            ["CF"] = -2,
            ["CP"] = -3,
            ["CS"] = -1,
            ["CT"] = -1,
            ["CW"] = -2,
            ["CY"] = -2,
            ["CV"] = -1,
            ["QA"] = -1,
            ["QR"] = 1,
            ["QN"] = 0,
            ["QD"] = 0,
            ["QC"] = -3,
            ["QQ"] = 5,
            ["QE"] = 2,
            ["QG"] = -2,
            ["QH"] = 0,
            ["QI"] = -3,
            ["QL"] = -2,
            ["QK"] = 1,
            ["QM"] = 0,
            ["QF"] = -3,
            ["QP"] = -1,
            ["QS"] = 0,
            ["QT"] = -1,
            ["QW"] = -2,
            ["QY"] = -1,
            ["QV"] = -2,
            ["EA"] = -1,
            ["ER"] = 0,
            ["EN"] = 0,
            ["ED"] = 2,
            ["EC"] = -4,
            ["EQ"] = 2,
            ["EE"] = 5,
            ["EG"] = -2,
            ["EH"] = 0,
            ["EI"] = -3,
            ["EL"] = -3,
            ["EK"] = 1,
            ["EM"] = -2,
            ["EF"] = -3,
            ["EP"] = -1,
            ["ES"] = 0,
            ["ET"] = -1,
            ["EW"] = -3,
            ["EY"] = -2,
            ["EV"] = -2,
            ["GA"] = 0,
            ["GR"] = -2,
            ["GN"] = 0,
            ["GD"] = -1,
            ["GC"] = -3,
            ["GQ"] = -2,
            ["GE"] = -2,
            ["GG"] = 6,
            ["GH"] = -2,
            ["GI"] = -4,
            ["GL"] = -4,
            ["GK"] = -2,
            ["GM"] = -3,
            ["GF"] = -3,
            ["GP"] = -2,
            ["GS"] = 0,
            ["GT"] = -2,
            ["GW"] = -2,
            ["GY"] = -3,
            ["GV"] = -3,
            ["HA"] = -2,
            ["HR"] = 0,
            ["HN"] = 1,
            ["HD"] = -1,
            ["HC"] = -3,
            ["HQ"] = 0,
            ["HE"] = 0,
            ["HG"] = -2,
            ["HH"] = 8,
            ["HI"] = -3,
            ["HL"] = -3,
            ["HK"] = -1,
            ["HM"] = -2,
            ["HF"] = -1,
            ["HP"] = -2,
            ["HS"] = -1,
            ["HT"] = -2,
            ["HW"] = -2,
            ["HY"] = 2,
            ["HV"] = -3,
            ["IA"] = -1,
            ["IR"] = -3,
            ["IN"] = -3,
            ["ID"] = -3,
            ["IC"] = -1,
            ["IQ"] = -3,
            ["IE"] = -3,
            ["IG"] = -4,
            ["IH"] = -3,
            ["II"] = 4,
            ["IL"] = 2,
            ["IK"] = -3,
            ["IM"] = 1,
            ["IF"] = 0,
            ["IP"] = -3,
            ["IS"] = -2,
            ["IT"] = -1,
            ["IW"] = -3,
            ["IY"] = -1,
            ["IV"] = 3,
            ["LA"] = -1,
            ["LR"] = -2,
            ["LN"] = -3,
            ["LD"] = -4,
            ["LC"] = -1,
            ["LQ"] = -2,
            ["LE"] = -3,
            ["LG"] = -4,
            ["LH"] = -3,
            ["LI"] = 2,
            ["LL"] = 4,
            ["LK"] = -2,
            ["LM"] = 2,
            ["LF"] = 0,
            ["LP"] = -3,
            ["LS"] = -2,
            ["LT"] = -1,
            ["LW"] = -2,
            ["LY"] = -1,
            ["LV"] = 1,
            ["KA"] = -1,
            ["KR"] = 2,
            ["KN"] = 0,
            ["KD"] = -1,
            ["KC"] = -3,
            ["KQ"] = 1,
            ["KE"] = 1,
            ["KG"] = -2,
            ["KH"] = -1,
            ["KI"] = -3,
            ["KL"] = -2,
            ["KK"] = 5,
            ["KM"] = -1,
            ["KF"] = -3,
            ["KP"] = -1,
            ["KS"] = 0,
            ["KT"] = -1,
            ["KW"] = -3,
            ["KY"] = -2,
            ["KV"] = -2,
            ["MA"] = -1,
            ["MR"] = -1,
            ["MN"] = -2,
            ["MD"] = -3,
            ["MC"] = -1,
            ["MQ"] = 0,
            ["ME"] = -2,
            ["MG"] = -3,
            ["MH"] = -2,
            ["MI"] = 1,
            ["ML"] = 2,
            ["MK"] = -1,
            ["MM"] = 5,
            ["MF"] = 0,
            ["MP"] = -2,
            ["MS"] = -1,
            ["MT"] = -1,
            ["MW"] = -1,
            ["MY"] = -1,
            ["MV"] = 1,
            ["FA"] = -2,
            ["FR"] = -3,
            ["FN"] = -3,
            ["FD"] = -3,
            ["FC"] = -2,
            ["FQ"] = -3,
            ["FE"] = -3,
            ["FG"] = -3,
            ["FH"] = -1,
            ["FI"] = 0,
            ["FL"] = 0,
            ["FK"] = -3,
            ["FM"] = 0,
            ["FF"] = 6,
            ["FP"] = -4,
            ["FS"] = -2,
            ["FT"] = -2,
            ["FW"] = 1,
            ["FY"] = 3,
            ["FV"] = -1,
            ["PA"] = -1,
            ["PR"] = -2,
            ["PN"] = -2,
            ["PD"] = -1,
            ["PC"] = -3,
            ["PQ"] = -1,
            ["PE"] = -1,
            ["PG"] = -2,
            ["PH"] = -2,
            ["PI"] = -3,
            ["PL"] = -3,
            ["PK"] = -1,
            ["PM"] = -2,
            ["PF"] = -4,
            ["PP"] = 7,
            ["PS"] = -1,
            ["PT"] = -1,
            ["PW"] = -4,
            ["PY"] = -3,
            ["PV"] = -2,
            ["SA"] = 1,
            ["SR"] = -1,
            ["SN"] = 1,
            ["SD"] = 0,
            ["SC"] = -1,
            ["SQ"] = 0,
            ["SE"] = 0,
            ["SG"] = 0,
            ["SH"] = -1,
            ["SI"] = -2,
            ["SL"] = -2,
            ["SK"] = 0,
            ["SM"] = -1,
            ["SF"] = -2,
            ["SP"] = -1,
            ["SS"] = 4,
            ["ST"] = 1,
            ["SW"] = -3,
            ["SY"] = -2,
            ["SV"] = -2,
            ["TA"] = 0,
            ["TR"] = -1,
            ["TN"] = 0,
            ["TD"] = -1,
            ["TC"] = -1,
            ["TQ"] = -1,
            ["TE"] = -1,
            ["TG"] = -2,
            ["TH"] = -2,
            ["TI"] = -1,
            ["TL"] = -1,
            ["TK"] = -1,
            ["TM"] = -1,
            ["TF"] = -2,
            ["TP"] = -1,
            ["TS"] = 1,
            ["TT"] = 5,
            ["TW"] = -2,
            ["TY"] = -2,
            ["TV"] = 0,
            ["WA"] = -3,
            ["WR"] = -3,
            ["WN"] = -4,
            ["WD"] = -4,
            ["WC"] = -2,
            ["WQ"] = -2,
            ["WE"] = -3,
            ["WG"] = -2,
            ["WH"] = -2,
            ["WI"] = -3,
            ["WL"] = -2,
            ["WK"] = -3,
            ["WM"] = -1,
            ["WF"] = 1,
            ["WP"] = -4,
            ["WS"] = -3,
            ["WT"] = -2,
            ["WW"] = 11,
            ["WY"] = 2,
            ["WV"] = -3,
            ["YA"] = -2,
            ["YR"] = -2,
            ["YN"] = -2,
            ["YD"] = -3,
            ["YC"] = -2,
            ["YQ"] = -1,
            ["YE"] = -2,
            ["YG"] = -3,
            ["YH"] = 2,
            ["YI"] = -1,
            ["YK"] = -2,
            ["YM"] = -1,
            ["YF"] = 3,
            ["YP"] = -3,
            ["YS"] = -2,
            ["YT"] = -2,
            ["YW"] = 2,
            ["YY"] = 7,
            ["YV"] = -1,
            ["VA"] = 0,
            ["VR"] = -3,
            ["VN"] = -3,
            ["VD"] = -3,
            ["VC"] = -1,
            ["VQ"] = -2,
            ["VE"] = -2,
            ["VG"] = -3,
            ["VH"] = -3,
            ["VI"] = 3,
            ["VL"] = 1,
            ["VK"] = -2,
            ["VM"] = 1,
            ["VF"] = -1,
            ["VP"] = -2,
            ["VS"] = -2,
            ["VT"] = 0,
            ["VW"] = -3,
            ["VY"] = -1,
            ["VV"] = 4

            #endregion
        };

        private static Direction[,] InitializeMatrix(ReadOnlySpan<char> top, ReadOnlySpan<char> left, sbyte penalty)
        {
            var scoreData = new int[left.Length + 1, top.Length + 1];
            var traverseData = new Direction[left.Length + 1, top.Length + 1];
            scoreData[0, 0] = 0;
            for (var j = 1; j <= left.Length; j++)
            {
                scoreData[j, 0] = j * penalty;
                traverseData[j, 0] = Direction.Up;
            }
            for (var i = 1; i <= top.Length; i++)
            {
                scoreData[0, i] = i * penalty;
                traverseData[0, i] = Direction.Left;
            }

            for (var i = 1; i <= top.Length; i++)
            {
                for (var j = 1; j <= left.Length; j++)
                {
                    var options = new (int, Direction)[3];
                    var i2 = i - 1;
                    var j2 = j - 1;
                    options[0] = (
                        scoreData[j2, i2] + Blossum62[new string(new[] { top[i2], left[j2] })],
                        Direction.Diagonal);
                    options[1] = (scoreData[j2, i] + penalty, Direction.Up);
                    options[2] = (scoreData[j, i2] + penalty, Direction.Left);
                    var (score, direction) = options.OrderByDescending(x => x.Item1).First();
                    scoreData[j, i] = score;
                    traverseData[j, i] = direction;
                }
            }

            return traverseData;
        }

        public static Task<(string first, string second)> Align(this Sequence first, Sequence second, sbyte penalty)
        {
            return Align(
                new string(first.GetData().Span),
                new string(second.GetData().Span),
                penalty);
        }

        public static Task<(string first, string second)> Align(this string first, string second, sbyte penalty)
        {
            return Task.Run(
                () =>
                {
                    var matrixData = InitializeMatrix(first.AsSpan(), second.AsSpan(), penalty);
                    var topStack = new Stack<char>(first.Length);
                    var leftStack = new Stack<char>(second.Length);
                    var j = matrixData.GetLength(0) - 1;
                    var i = matrixData.GetLength(1) - 1;
                    var direction = matrixData[j, i];
                    while (direction != Direction.Done)
                    {
                        direction = matrixData[j, i];

                        switch (direction)
                        {
                            case Direction.Diagonal:
                                topStack.Push(first[i - 1]);
                                leftStack.Push(second[j - 1]);
                                i -= 1;
                                j -= 1;
                                break;
                            case Direction.Left:
                                topStack.Push(first[i - 1]);
                                leftStack.Push('-');
                                i -= 1;
                                break;
                            case Direction.Up:
                                topStack.Push('-');
                                leftStack.Push(second[j - 1]);
                                j -= 1;
                                break;
                            case Direction.Done:
                                break;
                            default:
                                throw new InvalidOperationException("Invalid direction");
                        }
                    }

                    return (new string(topStack.ToArray()), new string(leftStack.ToArray()));
                });
        }

        public static int GetCombineIndex(this (string first, string second) alignment)
        {
            var start = alignment.first.StartsWith('-') ? alignment.second : alignment.first;
            var end = alignment.first.StartsWith('-') ? alignment.first : alignment.second;
            var trailingBlanks = start.Reverse().TakeWhile(c => c == '-').Count();
            var leadingBlanks = end.TakeWhile(c => c == '-').Count();
            var overlapEnd = start.Length - trailingBlanks;

            var s = start[..leadingBlanks];
            var o = end[leadingBlanks..overlapEnd];
            var startOverlap = start[..^trailingBlanks];
            var endOverlap = end.AsSpan(leadingBlanks);
            if (s + o == startOverlap && endOverlap.StartsWith(o))
            {
                return leadingBlanks;
            }

            return -1;
        }

        public static Sequence Combine(this Sequence first, Sequence second, string newId, int index)
        {
            var basePairs = first.Take(index).Concat(second).ToList();
            return new Sequence(
                newId,
                basePairs.Select(x => x.Letter).ToArray(),
                basePairs.Select(x => x.ErrorProbability).ToArray());
        }
    }
}