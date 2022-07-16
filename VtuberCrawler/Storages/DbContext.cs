using VtuberCrawler.Models;

namespace VtuberCrawler.Storages
{
    public class DbContext
    {
        public Storage<string, Vtuber> Vtubers { get; set; } = null!;
        public Storage<string, Data> Datas { get; set; } = null!;
    }
}
