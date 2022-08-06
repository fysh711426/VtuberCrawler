using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VtuberCrawler.Api
{
    public class JsonData
    {
        public int? Id { get; set; }
        public string? ChannelUrl { get; set; }
        public string? Name { get; set; }
        public string? Thumbnail { get; set; }
        public string? Subscribe { get; set; }
        public string? Score { get; set; }
        public string? VideoTitle { get; set; }
        public string? VideoViewCount { get; set; }
        public string? VideoThumbnail { get; set; }
        public string? VideoUrl { get; set; }
        public int? Rank { get; set; }
        public int? RankVar { get; set; }
    }
}
