﻿using System.Diagnostics;
using VtuberCrawler.Crawlers;
using VtuberCrawler.Storages;

namespace VtuberDataCrawler
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var action = "";
            var waitfor = false;
            if (args.Length == 0)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("VtuberData");
                Console.ResetColor();
                Console.Write(" > ");
                action = Console.ReadLine();
                waitfor = true;
            }
            else
            {
                if (args[0] == "auto")
                {
                    action = "data";
                    if (DateTime.Now.DayOfWeek == DayOfWeek.Monday)
                        action = "vtuber";
                }
                waitfor = false;
            }

            try
            {
                var workDir = AppDomain.CurrentDomain.BaseDirectory;
                var vtuberPath = Path.Combine(workDir, "Vtubers.csv");

                var now = DateTime.Now;
                var month = now.ToString("yyyy-MM");
                var time = now.ToString("yyyy-MM-dd_HH-mm-ss");
                var monthDir = Path.Combine(workDir, month);
                if (!Directory.Exists(monthDir))
                    Directory.CreateDirectory(monthDir);
                var dataPath = Path.Combine(monthDir, $"Data_{time}.csv");

                var db = new DbContext();
                db.Vtubers = new(vtuberPath, it => it.ChannelUrl);
                db.Datas = new(dataPath, it => it.ChannelUrl);

                var vtuberCrawler = new _VtuberCrawler(now, db);
                await vtuberCrawler.Load();

                if (action == "vtuber")
                {
                    await vtuberCrawler.CreateOrUpdateVtubersTw();
                    await vtuberCrawler.CreateOrUpdateVtubersJp();
                    await vtuberCrawler.Save();

                    var _time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var ts = DateTime.Parse(_time) - now;
                    var str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
                    Console.WriteLine($"[{_time}] Save vtubers success. @ {str}");
                }
                if (action == "vtuber" || action == "data")
                {
                    var dataCrawler = new DataCrawler(now, db);
                    await dataCrawler.CreateAndCalcData();
                    await dataCrawler.Save();
                    await vtuberCrawler.Save();

                    var _time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var ts = DateTime.Parse(_time) - now;
                    var str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
                    Console.WriteLine($"[{_time}] Save data success. @ {str}");
                }
                else
                {
                    throw new Exception("Wrong action.");
                }

                var info = new ProcessStartInfo(
                    Path.Combine(workDir, "auto_commit.bat"));
                info.WorkingDirectory = workDir;
                info.CreateNoWindow = false;
                info.UseShellExecute = false;
                var process = Process.Start(info);
                process?.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($" > {ex.Message}");
                Console.ResetColor();
                waitfor = true;
            }

            if (waitfor)
                Console.ReadLine();
        }
    }
}