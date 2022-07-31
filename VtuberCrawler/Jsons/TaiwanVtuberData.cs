using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VtuberCrawler.Jsons
{
    public class TaiwanVtuberData
    {
        public List<VTuber> VTubers { get; set; } = new();


        public class VTuber
        {
            public string name { get; set; } = "";
            public string nationality { get; set; } = "";
            public string activity { get; set; } = "";

            public YouTube YouTube { get; set; } = new();
        }

        public class YouTube
        {
            public string id { get; set; } = "";
        }
    }
}
