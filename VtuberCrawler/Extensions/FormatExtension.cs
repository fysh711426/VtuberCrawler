namespace VtuberCrawler.Extensions
{
    internal static class FormatExtension
    {
        public static string GetCountText(this long val)
        {
            if (val >= 100000000)
                return (val / 100000000).ToString("0.##") + "億";
            if (val >= 10000)
                return (val / 10000).ToString("0.##") + "萬";
            return val.ToString();
        }

        public static string GetCountSeparateText(this long val)
        {
            return val.ToString("#,0");
        }
    }
}
