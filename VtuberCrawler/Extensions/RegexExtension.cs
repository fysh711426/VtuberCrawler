﻿using System.Text.RegularExpressions;

namespace VtuberCrawler.Extensions
{
    internal static class RegexExtension
    {
        internal static TResult Select<TResult>(this Match match, Func<Match, TResult> selector)
        {
            return selector(match);
        }

        internal static IEnumerable<TResult> SelectMany<TResult>(this MatchCollection matches, Func<Match, TResult> selector)
        {
            foreach (Match match in matches)
            {
                yield return selector(match);
            }
        }

        internal static IEnumerable<TResult> SelectMany<TResult>(this MatchCollection matches, Func<Match, int, TResult> selector)
        {
            var index = 0;
            foreach (Match match in matches)
            {
                yield return selector(match, index);
                index++;
            }
        }
    }
}
