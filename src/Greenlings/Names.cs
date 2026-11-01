using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Short, gruff, slightly silly Norse-ish names: Grug, Skag, Skorby, Bjolf, Knurri...
    /// One chunky syllable, sometimes a cute ending.
    /// </summary>
    internal static class Names
    {
        private static readonly string[] Onsets =
        {
            "Gr", "Sk", "Sn", "Bj", "Kn", "Tr", "Fl", "Gl", "Br", "Dr", "Kr", "Sl", "Hr", "Thr", "Bl", "Sm", "St", "Sv",
            "G", "B", "K", "M", "N", "R", "T", "V", "H", "Fj", "Hj",
        };

        private static readonly string[] Vowels = { "u", "o", "a", "i", "e", "y", "au", "ei", "oo" };

        private static readonly string[] Codas =
        {
            "g", "gg", "k", "rk", "mp", "b", "bb", "nk", "sk", "ff", "d", "lf", "rn", "t", "rg", "lk", "mm", "p", "rb", "nd",
        };

        private static readonly string[] Endings = { "", "", "", "", "y", "i", "o", "le", "a", "ur", "kin" };

        public static string Generate()
        {
            string name;
            do
            {
                var onset = Pick(Onsets);
                var vowel = Pick(Vowels);
                var coda = Pick(Codas);
                var ending = Pick(Endings);
                // "Skorb" + "y" reads better than "Skorbby": a doubled coda drops a letter before an ending
                if (ending.Length > 0 && coda.Length == 2 && coda[0] == coda[1]) coda = coda.Substring(1);
                name = onset + vowel + coda + ending;
            } while (name.Length < 3 || name.Length > 7);
            return name;
        }

        private static string Pick(string[] options) => options[Random.Range(0, options.Length)];
    }
}
