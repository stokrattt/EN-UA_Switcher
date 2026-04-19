using System;
using Switcher.Core;

namespace EvalBugTest
{
    class Program
    {
        static void Main()
        {
            var res1 = CorrectionHeuristics.Evaluate("дштлю", CorrectionMode.Auto);
            var res2 = CorrectionHeuristics.Evaluate("ьйее", CorrectionMode.Auto);
            var res3 = CorrectionHeuristics.Evaluate("link.", CorrectionMode.Auto);
            
            Console.WriteLine($"дштлю -> {(res1?.ConvertedText ?? "NULL")}");
            Console.WriteLine($"ьйее -> {(res2?.ConvertedText ?? "NULL")}");
            Console.WriteLine($"link. -> {(res3?.ConvertedText ?? "NULL")}");
        }
    }
}