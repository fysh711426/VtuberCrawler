using System.Diagnostics;
using System.Text.RegularExpressions;
using VtuberCrawler.Api;
using VtuberCrawler.Crawlers;
using VtuberCrawler.Extensions;
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
                if (action == "vtuber" || action == "data" || action == "create api")
                {
                    var csvList = Directory.GetDirectories(workDir)
                        .Where(it => Regex.IsMatch(it, @"[\d]{4}-[\d]{2}"))
                        .OrderByDescending(it => it)
                        .Take(5)
                        .SelectMany(it => Directory.GetFiles(it))
                        .OrderByDescending(it => it)
                        .ToList();

                    var today = csvList.First()
                        .Pipe(it => Regex.Match(it, @"([\d]{4}-[\d]{2}-[\d]{2})"))
                        .Select(m => DateTime.Parse(m.Groups[1].Value).Date);

                    var apiPath = Path.Combine(workDir, "api");
                    if (!Directory.Exists(apiPath))
                        Directory.CreateDirectory(apiPath);

                    var creator = new JsonDataCreator(apiPath, vtuberPath, today);
                    creator.Init(csvList);
                    await creator.Update();

                    var _time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var ts = DateTime.Parse(_time) - now;
                    var str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
                    Console.WriteLine($"[{_time}] Save api success. @ {str}");
                }
                else if(action == "update model")
                {
                    var _vtuberCrawler = new _VtuberCrawler(now, db);
                    await _vtuberCrawler.Load(true);
                    await _vtuberCrawler.Save();

                    var csvList = Directory.GetDirectories(workDir)
                        .SelectMany(it => Directory.GetFiles(it))
                        .Where(it => Regex.IsMatch(it, @"[\d]{4}-[\d]{2}"))
                        .ToList();
                    foreach (var path in csvList)
                    {
                        var _db = new DbContext();
                        _db.Datas = new(path, it => it.ChannelUrl);
                        var dataCrawler = new DataCrawler(now, _db);
                        await dataCrawler.Load(true);
                        await dataCrawler.Save();
                    }

                    var _time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var ts = DateTime.Parse(_time) - now;
                    var str = (ts.Hours.ToString("00") == "00" ? "" : ts.Hours.ToString("00") + "h") + ts.Minutes.ToString("00") + "m" + ts.Seconds.ToString("00") + "s";
                    Console.WriteLine($"[{_time}] Update model success. @ {str}");
                }
                else
                {
                    throw new Exception("Wrong action.");
                }

                if (args.FirstOrDefault() == "auto")
                {
                    var info = new ProcessStartInfo(
                    Path.Combine(workDir, "auto_commit.bat"));
                    info.WorkingDirectory = workDir;
                    info.CreateNoWindow = false;
                    info.UseShellExecute = false;
                    var process = Process.Start(info);
                    process?.WaitForExit();
                }
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