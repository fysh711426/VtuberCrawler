namespace VtuberCrawler.Extensions
{
    internal static class LinqExtension
    {
        public static IEnumerable<TSource> BreakOn<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            foreach (var item in source)
            {
                if (predicate(item))
                    break;
                yield return item;
            }
        }

        public static IEnumerable<TSource> BreakOnNext<TSource>(this IEnumerable<TSource> source, Func<TSource, bool> predicate)
        {
            var isBreak = false;
            foreach (var item in source)
            {
                if (isBreak)
                    break;
                if (predicate(item))
                    isBreak = true;
                yield return item;
            }
        }

        public static IEnumerable<RankItem<TSource>> Rank<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector) => source.OrderBy(selector)._rank(selector);

        public static IEnumerable<RankItem<TSource>> RankDesc<TSource, TKey>(this IEnumerable<TSource> source,
            Func<TSource, TKey> selector) => source.OrderByDescending(selector)._rank(selector);

        private static IEnumerable<RankItem<TSource>> _rank<TSource, TKey>(this IEnumerable<TSource> source, 
            Func<TSource, TKey> selector)
        {
            var rank = 1;
            var count = 1;
            var first = true;
            var prev = default(TSource)!;

            foreach (var item in source)
            {
                if (first)
                {
                    yield return new RankItem<TSource>(rank, item);
                    first = false;
                    prev = item;
                    continue;
                }
                if (EqualityComparer<TKey>.Default.Equals(selector(item), selector(prev)))
                {
                    yield return new RankItem<TSource>(rank, item);
                    count++;
                    prev = item;
                }
                else
                {
                    rank += count;
                    yield return new RankItem<TSource>(rank, item);
                    count = 0;
                    prev = item;
                }
            }
        }
    }
}
