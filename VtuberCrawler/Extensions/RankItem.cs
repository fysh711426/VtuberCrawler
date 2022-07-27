namespace VtuberCrawler.Extensions
{
    public class RankItem<T>
    {
        public int Rank { get; set; }
        public T Item { get; set; }
        public RankItem(int rank, T item)
        {
            Rank = rank;
            Item = item;
        }
    }
}
