﻿using Newtonsoft.Json;
using System.Text.RegularExpressions;
using VtuberCrawler.Extensions;
using VtuberCrawler.Jsons;
using VtuberCrawler.Models;
using VtuberCrawler.Storages;
using YoutubeParser;
using YoutubeParser.Channels;

namespace VtuberCrawler.Crawlers
{
    public class _VtuberCrawler : BaseCrawler
    {
        private int _id = 1;
        private DateTime _now;
        private DbContext _db;

        public _VtuberCrawler(DateTime now, DbContext db)
        {
            _now = now;
            _db = db;
        }

        public async Task Load(bool ignoreMissing = false)
        {
            _id = 1;
            await _db.Vtubers.Load(ignoreMissing);
        }

        public async Task Save()
        {
            await _db.Vtubers.Save(list =>
            {
                var maxId = list
                    .Where(it => it.Id > 0)
                    .OrderByDescending(it => it.Id)
                    .FirstOrDefault()?.Id ?? 0;
                foreach (var item in list)
                {
                    if (item.Id < 0)
                        item.Id = maxId + item.Id * -1;
                }
                return list.OrderBy(it => it.Id);
            });
        }

        public async Task CreateOrUpdateVtubersTw()
        {
            IEnumerable<(string url, string thumb, string name, string abbr, string youtubeUrl, string status)>
                MapVtuber(string html)
            {
                return html
                    .Pipe(it => Regex.Match(it, @"<section.*?>([\s\S]*?)<\/section>"))
                    .Select(m => m.Groups[1].Value)
                    .Pipe(it => Regex.Matches(it, @"<div class=""d-flex"">([\s\S]*?)<span class=""position-absolute py-1"">(.*?)<\/span>"))
                    .SelectMany(m => m.Value)
                    .Pipes(it => it
                        .Pipe(itt => Regex.Match(itt, @"href=""([\s\S]*?)""[\s\S]*?src=""([\s\S]*?)""[\s\S]*?person-name"">([\s\S]*?)<\/a>[\s\S]*?person-abbr"">([\s\S]*?)<\/div>([\s\S]*?)py-1"">([\s\S]*?)<\/span>"))
                        .Select<(string url, string thumb, string name, string abbr, string youtubeUrl, string status)>(m =>
                            (m.Groups[1].Value.Trim(),
                             m.Groups[2].Value.Trim(),
                             m.Groups[3].Value.Trim(),
                             m.Groups[4].Value.Trim(),
                             m.Groups[5].Value
                                .Pipe(ittt => Regex.Match(ittt, @"href=""(https:\/\/www\.youtube.*?)"""))
                                .Select(m => m.Groups[1].Value),
                             m.Groups[6].Value.Trim())));
            }

            // Update Vtuber Data
            {
                using var response = await Retry(() =>
                {
                    var clinet = _httpClient;
                    var url = "https://vt.cdein.cc/list/?a=a&o=D";
                    return clinet.GetAsync(url);
                });
                var html = await response.Content.ReadAsStringAsync();
                var vtubers = MapVtuber(html).ToList();

                Status getStatus(string status)
                {
                    if (status == "準備中")
                        return Status.Prepare;
                    if (status == "停止活動")
                        return Status.Graduate;
                    return Status.Activity;
                }

                var index = 0;
                var count = vtubers.Count;
                foreach (var item in vtubers)
                {
                    index++;
                    if (item.youtubeUrl == "")
                        continue;

                    var model = _db.Vtubers.Get(item.youtubeUrl);
                    if (model != null)
                        if (model.Status != Status.Prepare &&
                            model.Status != Status.Activity)
                            continue;

                    var data = _db.Datas.Get(item.youtubeUrl);
                    if (data != null)
                        continue;

                    var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var youtube = new YoutubeClient(() => DelayRandom());
                    var info = null as Channel;
                    try
                    {
                        info = await Retry(() =>
                            youtube.Channel.GetAsync(item.youtubeUrl));
                    }
                    catch (Exception ex)
                    {
                        var channelId = item.youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                        Console.WriteLine($"[Error] {channelId}");
                        Console.WriteLine(ex.Message);
                        await SleepRandom();
                        continue;
                    }
                    var status = getStatus(item.status);
                    if (info.Title == "")
                        status = Status.NotFound;
                    if (model == null)
                    {
                        model = new Vtuber();
                        model.Id = (_id++) * -1;
                        model.ChannelUrl = item.youtubeUrl;
                        model.Name = item.name;
                        model.Area = "TW";
                        model.Status = status;
                        model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss");
                        model.ChannelName = info.Title;
                        model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                        _db.Vtubers.CreateOrUpdate(model);
                        Console.WriteLine($"[{time}][{index}/{count}] Create tw vtuber {model.Name}");
                    }
                    else
                    {
                        if (model.Status == Status.Prepare ||
                            model.Status == Status.Activity)
                        {
                            // If the status changed from prepare to activity, update the time.
                            if (status == Status.Activity && model.Status == Status.Prepare)
                                model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss");
                            //if (item.name != "")
                            //    model.Name = item.name;
                            if (info.Title != "")
                                model.ChannelName = info.Title;
                            if (info.Thumbnails.Count > 0)
                                model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            if (model.Area == "")
                                model.Area = "TW";
                            if (model.Status < status)
                                model.Status = status;
                            Console.WriteLine($"[{time}][{index}/{count}] Update tw vtuber {model.Name}");
                        }
                    }
                    data = new Data
                    {
                        ChannelUrl = model.ChannelUrl,
                        SubscriberCount = info.SubscriberCount,
                        ViewCount = info.ViewCount
                    };
                    _db.Datas.CreateOrUpdate(data);
                    await SleepRandom();
                }
            }

            // Update Area Data
            {
                var tags = new List<(string name, string area)>
                {
                    ("香港", "HK"),
                    ("馬來西亞", "MY")
                };
                foreach (var tag in tags)
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = $"https://vt.cdein.cc/tag/{tag.name}";
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = MapVtuber(html);

                    foreach (var item in vtubers)
                    {
                        if (item.youtubeUrl == "")
                            continue;

                        var model = _db.Vtubers.Get(item.youtubeUrl);
                        if (model == null)
                            continue;

                        //if (model.Id < 0)
                        {
                            model.Area = tag.area;
                            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            Console.WriteLine($"[{time}] Update area {model.Name}");
                        }
                    }
                }
            }
        }

        public async Task CreateOrUpdateVtubersFromTwVtData()
        {
            // Update Vtuber Data
            {
                var clinet = _httpClient;

                using var response = await Retry(() =>
                {
                    var url = "https://api.github.com/repos/TaiwanVtuberData/TaiwanVTuberTrackingDataJson/commits/master";
                    return clinet.GetAsync(url);
                });
                var html = await response.Content.ReadAsStringAsync();

                var api = html
                    .Pipe(it => Regex.Match(it, @"""sha"": ""(.*?)"""))
                    .Select(m => m.Groups[1].Value)
                    .Pipe(it => $"https://cdn.statically.io/gh/TaiwanVtuberData/TaiwanVTuberTrackingDataJson/{it}/api/v2/all/vtubers/all.json");

                using var _response = await Retry(() =>
                {
                    return clinet.GetAsync(api);
                });
                var json = await _response.Content.ReadAsStringAsync();

                var vtubers = JsonConvert.DeserializeObject<TaiwanVtuberData>(json)?
                    .VTubers.ToList() ?? new List<TaiwanVtuberData.VTuber>();

                Status getStatus(string activity)
                {
                    if (activity == "preparing")
                        return Status.Prepare;
                    if (activity == "graduate")
                        return Status.Graduate;
                    return Status.Activity;
                }

                string getArea(string nationality)
                {
                    if (nationality == "UNKNOWN")
                        return "";
                    return nationality;
                }

                var index = 0;
                var count = vtubers.Count;
                foreach (var item in vtubers)
                {
                    index++;
                    if (item.YouTube.id == "")
                        continue;

                    var youtubeUrl = $"https://www.youtube.com/channel/{item.YouTube.id}";

                    var model = _db.Vtubers.Get(youtubeUrl);
                    if (model != null)
                        if (model.Status != Status.Prepare &&
                            model.Status != Status.Activity)
                            continue;

                    var data = _db.Datas.Get(youtubeUrl);
                    if (data != null)
                        continue;

                    var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    var youtube = new YoutubeClient(() => DelayRandom());
                    var info = null as Channel;
                    try
                    {
                        info = await Retry(() =>
                            youtube.Channel.GetAsync(youtubeUrl));
                    }
                    catch (Exception ex)
                    {
                        var channelId = youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                        Console.WriteLine($"[Error] {channelId}");
                        Console.WriteLine(ex.Message);
                        await SleepRandom();
                        continue;
                    }
                    var status = getStatus(item.activity);
                    if (info.Title == "")
                        status = Status.NotFound;
                    var area = getArea(item.nationality);
                    if (model == null)
                    {
                        model = new Vtuber();
                        model.Id = (_id++) * -1;
                        model.ChannelUrl = youtubeUrl;
                        model.Name = item.name;
                        model.Area = area;
                        model.Status = status;
                        model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss");
                        model.ChannelName = info.Title;
                        model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                        _db.Vtubers.CreateOrUpdate(model);
                        Console.WriteLine($"[{time}][{index}/{count}] Create tw vtuber {model.Name}");
                    }
                    else
                    {
                        if (model.Status == Status.Prepare ||
                            model.Status == Status.Activity)
                        {
                            // If the status changed from prepare to activity, update the time.
                            if (status == Status.Activity && model.Status == Status.Prepare)
                                model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss");
                            //if (item.name != "")
                            //    model.Name = item.name;
                            if (info.Title != "")
                                model.ChannelName = info.Title;
                            if (info.Thumbnails.Count > 0)
                                model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            if (model.Area == "")
                                model.Area = area;
                            if (model.Status < status)
                                model.Status = status;
                            Console.WriteLine($"[{time}][{index}/{count}] Update tw vtuber {model.Name}");
                        }
                    }
                    data = new Data
                    {
                        ChannelUrl = model.ChannelUrl,
                        SubscriberCount = info.SubscriberCount,
                        ViewCount = info.ViewCount
                    };
                    _db.Datas.CreateOrUpdate(data);
                    await SleepRandom();
                }
            }
        }

        private Dictionary<string, string> _cacheChannelUrl = new Dictionary<string, string>();
        public async Task CreateOrUpdateVtubersJp()
        {
            _cacheChannelUrl = new Dictionary<string, string>();

            IEnumerable<(string userId, string name, string group)>
                MapVtuber(string html)
            {
                return html
                    .Pipe(it => Regex.Match(it, @"<table([\s\S]*?)<script>"))
                    .Select(m => m.Groups[1].Value)
                    .Pipe(it => Regex.Matches(it, @"<\/strong>[\s\S]*?href=""\/user\/(.*?)"".*?>([\s\S]*?)<\/a>[\s\S]*?<a.*?>(.*?)<\/a>"))
                    .SelectMany<(string userId, string name, string group)>(m => (
                        m.Groups[1].Value.Trim(),
                        m.Groups[2].Value.Trim(),
                        m.Groups[3].Value.Trim()
                    ));
            }

            // Update Vtuber Data JP
            {
                var index = 0;
                var count = 2000;
                var page = 40;
                for (var i = 0; i < page; i++)
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = $"https://virtual-youtuber.userlocal.jp/document/ranking?page={i + 1}";
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = html
                        .Pipe(it => Regex.Match(it, @"<table([\s\S]*?)<script>"))
                        .Select(m => m.Groups[1].Value)
                        .Pipe(it => Regex.Matches(it, @"<\/strong>[\s\S]*?href=""\/user\/(.*?)"".*?>([\s\S]*?)<\/a>"))
                        .SelectMany<(string userId, string name)>(m => (
                            m.Groups[1].Value.Trim(),
                            m.Groups[2].Value.Trim()
                        ));

                    foreach (var item in vtubers)
                    {
                        index++;

                        using var _response = await Retry(() =>
                        {
                            var clinet = _httpClient;
                            var url = $"https://virtual-youtuber.userlocal.jp/schedules/new?youtube={item.userId}";
                            return clinet.GetAsync(url);
                        });
                        var _html = await _response.Content.ReadAsStringAsync();

                        var youtubeUrl = Regex.Match(_html, @"<input size=""64.*?value=""(https:\/\/www\.youtube\.com\/channel\/.*?)"" name=""live_schedule")
                            .Groups[1].Value.Trim();
                        if (youtubeUrl == "")
                            continue;

                        var model = _db.Vtubers.Get(youtubeUrl);
                        if (model != null)
                            if (model.Status != Status.Prepare &&
                                model.Status != Status.Activity)
                                continue;

                        var data = _db.Datas.Get(youtubeUrl);
                        if (data != null)
                            continue;

                        _cacheChannelUrl[item.userId] = youtubeUrl;

                        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var youtube = new YoutubeClient(() => DelayRandom());
                        var info = null as Channel;
                        try
                        {
                            info = await Retry(() =>
                                youtube.Channel.GetAsync(youtubeUrl));
                        }
                        catch (Exception ex)
                        {
                            var channelId = youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                            Console.WriteLine($"[Error] {channelId}");
                            Console.WriteLine(ex.Message);
                            await SleepRandom();
                            continue;
                        }
                        var status = Status.Activity;
                        if (info.Title == "")
                            status = Status.NotFound;
                        if (model == null)
                        {
                            model = new Vtuber();
                            model.Id = (_id++) * -1;
                            model.ChannelUrl = youtubeUrl;
                            model.Name = item.name;
                            model.Status = status;
                            model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss"); ;
                            model.ChannelName = info.Title;
                            model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            _db.Vtubers.CreateOrUpdate(model);
                            Console.WriteLine($"[{time}][{index}/{count}] Create jp vtuber {model.Name}");
                        }
                        else
                        {
                            if (model.Status == Status.Prepare ||
                                model.Status == Status.Activity)
                            {
                                //if (item.name != "")
                                //    model.Name = item.name;
                                if (info.Title != "")
                                    model.ChannelName = info.Title;
                                if (info.Thumbnails.Count > 0)
                                    model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                                Console.WriteLine($"[{time}][{index}/{count}] Update jp vtuber {model.Name}");
                            }
                        }
                        data = new Data
                        {
                            ChannelUrl = model.ChannelUrl,
                            SubscriberCount = info.SubscriberCount,
                            ViewCount = info.ViewCount
                        };
                        _db.Datas.CreateOrUpdate(data);
                        await SleepRandom();
                    }
                }
            }

            // Update Company Data
            {
                // Hololive
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = $"https://virtual-youtuber.userlocal.jp/office/hololive_all";
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = MapVtuber(html);

                    string getGroup(string group)
                    {
                        if (group == "ホロライブ")
                            return "JP";
                        if (group == "hololive English")
                            return "EN";
                        if (group == "hololive Indonesia")
                            return "ID";
                        if (group == "ホロスターズ")
                            return "Stars";
                        return "";
                    }

                    foreach (var item in vtubers)
                    {
                        if (!_cacheChannelUrl.ContainsKey(item.userId))
                            continue;

                        var youtubeUrl = _cacheChannelUrl[item.userId];

                        var model = _db.Vtubers.Get(youtubeUrl);
                        if (model == null)
                            continue;

                        if (model.Id < 0)
                        {
                            model.Company = "Hololive";
                            model.Group = getGroup(item.group);
                            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            Console.WriteLine($"[{time}] Update company {model.Name}");
                        }
                        await SleepRandom();
                    }
                }

                // NIJISANJI
                {
                    var urls = new string[]
                    {
                        "https://virtual-youtuber.userlocal.jp/office/nijisanji_all",
                        "https://virtual-youtuber.userlocal.jp/office/nijisanji_world"
                    };

                    string getGroup(string group)
                    {
                        if (group == "にじさんじ(1・2期生)")
                            return "1、2 期生";
                        if (group == "にじさんじ(統合後)")
                            return "統合後";
                        if (group == "にじさんじ(ゲーマーズ出身)")
                            return "Gamers";
                        if (group == "にじさんじ(SEEDs出身)")
                            return "SEEDs";
                        if (group == "NIJISANJI EN")
                            return "EN";
                        if (group == "NIJISANJI ID")
                            return "ID";
                        if (group == "NIJISANJI KR")
                            return "KR";
                        return "";
                    }

                    foreach (var url in urls)
                    {
                        using var response = await Retry(() =>
                        {
                            var clinet = _httpClient;
                            return clinet.GetAsync(url);
                        });
                        var html = await response.Content.ReadAsStringAsync();
                        var vtubers = MapVtuber(html);

                        foreach (var item in vtubers)
                        {
                            if (!_cacheChannelUrl.ContainsKey(item.userId))
                                continue;

                            var youtubeUrl = _cacheChannelUrl[item.userId];

                            var model = _db.Vtubers.Get(youtubeUrl);
                            if (model == null)
                                continue;

                            if (model.Id < 0)
                            {
                                model.Company = "彩虹社";
                                model.Group = getGroup(item.group);
                                var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                                Console.WriteLine($"[{time}] Update company {model.Name}");
                            }
                            await SleepRandom();
                        }
                    }
                }
            }
        }

        public async Task CreateOrUpdateVtubersJpFromHololist()
        {
            _cacheChannelUrl = new Dictionary<string, string>();

            // Update Vtuber Data Hololive
            {
                var index = 0;

                var nextChapterUrl = $"https://hololist.net/hololive-popularity-ranking/";

                while (true)
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = nextChapterUrl;
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = html
                        .Pipe(it => Regex.Match(it, @"class=""row""([\s\S]*?)pagination"))
                        .Select(m => m.Groups[1].Value)
                        .Pipe(it => Regex.Matches(it, @"https:\/\/hololist\.net\/(.*?)\/"" title=""(.*?)"""))
                        .SelectMany<(string userId, string name)>(m => (
                            m.Groups[1].Value.Trim(),
                            m.Groups[2].Value.Trim()
                        ));

                    string getGroup(string group)
                    {
                        if (group == "Japan (JP)")
                            return "JP";
                        //if (group == "hololive English")
                        //    return "EN";
                        if (group == "Indonesia (ID)")
                            return "ID";
                        return "";
                    }

                    foreach (var item in vtubers)
                    {
                        index++;

                        using var _response = await Retry(() =>
                        {
                            var clinet = _httpClient;
                            var url = $"https://hololist.net/{item.userId}/";
                            return clinet.GetAsync(url);
                        });
                        var _html = await _response.Content.ReadAsStringAsync();

                        var youtubeUrl = Regex.Match(_html, @"(https:\/\/www\.youtube\.com\/channel\/.*?)\?")
                            .Groups[1].Value.Trim();
                        if (youtubeUrl == "")
                            continue;

                        var model = _db.Vtubers.Get(youtubeUrl);
                        if (model != null)
                            if (model.Status != Status.Prepare &&
                                model.Status != Status.Activity)
                                continue;

                        var data = _db.Datas.Get(youtubeUrl);
                        if (data != null)
                            continue;

                        _cacheChannelUrl[item.userId] = youtubeUrl;

                        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var youtube = new YoutubeClient(() => DelayRandom());
                        var info = null as Channel;
                        try
                        {
                            info = await Retry(() =>
                                youtube.Channel.GetAsync(youtubeUrl));
                        }
                        catch (Exception ex)
                        {
                            var channelId = youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                            Console.WriteLine($"[Error] {channelId}");
                            Console.WriteLine(ex.Message);
                            await SleepRandom();
                            continue;
                        }
                        var status = Status.Activity;
                        if (info.Title == "")
                            status = Status.NotFound;
                        if (model == null)
                        {
                            model = new Vtuber();
                            model.Id = (_id++) * -1;
                            model.ChannelUrl = youtubeUrl;

                            //----- Update Name -----
                            var name = Regex.Match(_html, @"Original Name<\/h2>([\s\S]*?)<\/div>")
                                .Groups[1].Value.Trim();
                            model.Name = !string.IsNullOrEmpty(name) ? name : item.name;
                            //model.Name = item.name;
                            //----- Update Name -----

                            //----- Update Company -----
                            model.Company = "Hololive";

                            var group = _html
                                .Pipe(it => Regex.Match(it, @"(Category<\/h2>[\s\S]*?<\/div>)"))
                                .Select(m => m.Groups[1].Value)
                                .Pipe(it => Regex.Match(it, @"Category<\/h2>[\s\S]*?title=""(.*?)"""))
                                .Select(m => m.Groups[1].Value);
                            //var group = Regex.Match(_html, @"Category<\/h2>[\s\S]*?title=""(.*?)""")
                            //    .Groups[1].Value.Trim();
                            model.Group = getGroup(group);
                            //----- Update Company -----

                            model.Status = status;
                            model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss"); ;
                            model.ChannelName = info.Title;
                            model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            _db.Vtubers.CreateOrUpdate(model);
                            //Console.WriteLine($"[{time}][{index}/{count}] Create jp vtuber {model.Name}");
                            Console.WriteLine($"[{time}][{index}] Create jp vtuber {model.Name}");
                        }
                        else
                        {
                            if (model.Status == Status.Prepare ||
                                model.Status == Status.Activity)
                            {
                                //if (item.name != "")
                                //    model.Name = item.name;
                                if (info.Title != "")
                                    model.ChannelName = info.Title;
                                if (info.Thumbnails.Count > 0)
                                    model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                                //Console.WriteLine($"[{time}][{index}/{count}] Update jp vtuber {model.Name}");
                                Console.WriteLine($"[{time}][{index}] Update jp vtuber {model.Name}");
                            }
                        }
                        data = new Data
                        {
                            ChannelUrl = model.ChannelUrl,
                            SubscriberCount = info.SubscriberCount,
                            ViewCount = info.ViewCount
                        };
                        _db.Datas.CreateOrUpdate(data);
                        await SleepRandom();
                    }

                    //找下一頁網址
                    var nextChapter = html
                        .Pipe(it => Regex.Match(it, @"href=""(.*?)"" >Next"))
                        .Select(m => new
                        {
                            url = m.Groups[1].Value
                        })
                        .Pipe(it => it.url != "" ? it : null);

                    bool isFinish()
                    {
                        if (nextChapter == null)
                            return true;
                        return false;
                    }
                    if (isFinish())
                        break;
                    nextChapterUrl = nextChapter!.url;
                }
            }

            // Update Vtuber Data NIJISANJI
            {
                var index = 0;

                var nextChapterUrl = $"https://hololist.net/nijisanji-popularity-ranking/";

                while (true)
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = nextChapterUrl;
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = html
                        .Pipe(it => Regex.Match(it, @"class=""row""([\s\S]*?)pagination"))
                        .Select(m => m.Groups[1].Value)
                        .Pipe(it => Regex.Matches(it, @"https:\/\/hololist\.net\/(.*?)\/"" title=""(.*?)"""))
                        .SelectMany<(string userId, string name)>(m => (
                            m.Groups[1].Value.Trim(),
                            m.Groups[2].Value.Trim()
                        ));

                    foreach (var item in vtubers)
                    {
                        index++;

                        using var _response = await Retry(() =>
                        {
                            var clinet = _httpClient;
                            var url = $"https://hololist.net/{item.userId}/";
                            return clinet.GetAsync(url);
                        });
                        var _html = await _response.Content.ReadAsStringAsync();

                        var youtubeUrl = Regex.Match(_html, @"(https:\/\/www\.youtube\.com\/channel\/.*?)\?")
                            .Groups[1].Value.Trim();
                        if (youtubeUrl == "")
                            continue;

                        var model = _db.Vtubers.Get(youtubeUrl);
                        if (model != null)
                            if (model.Status != Status.Prepare &&
                                model.Status != Status.Activity)
                                continue;

                        var data = _db.Datas.Get(youtubeUrl);
                        if (data != null)
                            continue;

                        _cacheChannelUrl[item.userId] = youtubeUrl;

                        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var youtube = new YoutubeClient(() => DelayRandom());
                        var info = null as Channel;
                        try
                        {
                            info = await Retry(() =>
                                youtube.Channel.GetAsync(youtubeUrl));
                        }
                        catch (Exception ex)
                        {
                            var channelId = youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                            Console.WriteLine($"[Error] {channelId}");
                            Console.WriteLine(ex.Message);
                            await SleepRandom();
                            continue;
                        }
                        var status = Status.Activity;
                        if (info.Title == "")
                            status = Status.NotFound;
                        if (model == null)
                        {
                            model = new Vtuber();
                            model.Id = (_id++) * -1;
                            model.ChannelUrl = youtubeUrl;

                            //----- Update Name -----
                            var name = Regex.Match(_html, @"Original Name<\/h2>([\s\S]*?)<\/div>")
                                .Groups[1].Value.Trim();
                            model.Name = !string.IsNullOrEmpty(name) ? name : item.name;
                            //model.Name = item.name;
                            //----- Update Name -----

                            //----- Update Company -----
                            model.Company = "彩虹社";

                            var group = _html
                                .Pipe(it => Regex.Match(it, @"(Category<\/h2>[\s\S]*?<\/div>)"))
                                .Select(m => m.Groups[1].Value)
                                .Pipe(it => Regex.Match(it, @"Category<\/h2>[\s\S]*?title=""(.*?)"""))
                                .Select(m => m.Groups[1].Value);
                            //----- Update Company -----

                            model.Status = status;
                            model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss"); ;
                            model.ChannelName = info.Title;
                            model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            _db.Vtubers.CreateOrUpdate(model);
                            //Console.WriteLine($"[{time}][{index}/{count}] Create jp vtuber {model.Name}");
                            Console.WriteLine($"[{time}][{index}] Create jp vtuber {model.Name}");
                        }
                        else
                        {
                            if (model.Status == Status.Prepare ||
                                model.Status == Status.Activity)
                            {
                                //if (item.name != "")
                                //    model.Name = item.name;
                                if (info.Title != "")
                                    model.ChannelName = info.Title;
                                if (info.Thumbnails.Count > 0)
                                    model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                                //Console.WriteLine($"[{time}][{index}/{count}] Update jp vtuber {model.Name}");
                                Console.WriteLine($"[{time}][{index}] Update jp vtuber {model.Name}");
                            }
                        }
                        data = new Data
                        {
                            ChannelUrl = model.ChannelUrl,
                            SubscriberCount = info.SubscriberCount,
                            ViewCount = info.ViewCount
                        };
                        _db.Datas.CreateOrUpdate(data);
                        await SleepRandom();
                    }

                    //找下一頁網址
                    var nextChapter = html
                        .Pipe(it => Regex.Match(it, @"href=""(.*?)"" >Next"))
                        .Select(m => new
                        {
                            url = m.Groups[1].Value
                        })
                        .Pipe(it => it.url != "" ? it : null);

                    bool isFinish()
                    {
                        if (nextChapter == null)
                            return true;
                        return false;
                    }
                    if (isFinish())
                        break;
                    nextChapterUrl = nextChapter!.url;
                }
            }

            // Update Vtuber Data JP
            {
                var index = 0;

                var nextChapterUrl = $"https://hololist.net/category/jp/";

                while (true)
                {
                    using var response = await Retry(() =>
                    {
                        var clinet = _httpClient;
                        var url = nextChapterUrl;
                        return clinet.GetAsync(url);
                    });
                    var html = await response.Content.ReadAsStringAsync();
                    var vtubers = html
                        .Pipe(it => Regex.Match(it, @"class=""row""([\s\S]*?)pagination"))
                        .Select(m => m.Groups[1].Value)
                        .Pipe(it => Regex.Matches(it, @"https:\/\/hololist\.net\/(.*?)\/"" title=""(.*?)"""))
                        .SelectMany<(string userId, string name)>(m => (
                            m.Groups[1].Value.Trim(),
                            m.Groups[2].Value.Trim()
                        ));

                    foreach (var item in vtubers)
                    {
                        if (_cacheChannelUrl.ContainsKey(item.userId))
                            continue;

                        index++;

                        using var _response = await Retry(() =>
                        {
                            var clinet = _httpClient;
                            var url = $"https://hololist.net/{item.userId}/";
                            return clinet.GetAsync(url);
                        });
                        var _html = await _response.Content.ReadAsStringAsync();

                        var youtubeUrl = Regex.Match(_html, @"(https:\/\/www\.youtube\.com\/channel\/.*?)\?")
                            .Groups[1].Value.Trim();
                        if (youtubeUrl == "")
                            continue;

                        var model = _db.Vtubers.Get(youtubeUrl);
                        if (model != null)
                            if (model.Status != Status.Prepare &&
                                model.Status != Status.Activity)
                                continue;

                        var data = _db.Datas.Get(youtubeUrl);
                        if (data != null)
                            continue;

                        var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        var youtube = new YoutubeClient(() => DelayRandom());
                        var info = null as Channel;
                        try
                        {
                            info = await Retry(() =>
                                youtube.Channel.GetAsync(youtubeUrl));
                        }
                        catch (Exception ex)
                        {
                            var channelId = youtubeUrl.Replace("https://www.youtube.com/channel/", "");
                            Console.WriteLine($"[Error] {channelId}");
                            Console.WriteLine(ex.Message);
                            await SleepRandom();
                            continue;
                        }
                        var status = Status.Activity;
                        if (info.Title == "")
                            status = Status.NotFound;
                        if (model == null)
                        {
                            model = new Vtuber();
                            model.Id = (_id++) * -1;
                            model.ChannelUrl = youtubeUrl;

                            //----- Update Name -----
                            var name = Regex.Match(_html, @"Original Name<\/h2>([\s\S]*?)<\/div>")
                                .Groups[1].Value.Trim();
                            model.Name = !string.IsNullOrEmpty(name) ? name : item.name;
                            //model.Name = item.name;
                            //----- Update Name -----

                            //----- Update Company -----
                            var group = _html
                                .Pipe(it => Regex.Match(it, @"(Category<\/h2>[\s\S]*?<\/div>)"))
                                .Select(m => m.Groups[1].Value)
                                .Pipe(it => Regex.Match(it, @"Category<\/h2>[\s\S]*?title=""(.*?)"""))
                                .Select(m => m.Groups[1].Value);
                            //----- Update Company -----

                            model.Status = status;
                            model.CreateTime = _now.ToString("yyyy-MM-dd HH:mm:ss"); ;
                            model.ChannelName = info.Title;
                            model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                            _db.Vtubers.CreateOrUpdate(model);
                            //Console.WriteLine($"[{time}][{index}/{count}] Create jp vtuber {model.Name}");
                            Console.WriteLine($"[{time}][{index}] Create jp vtuber {model.Name}");
                        }
                        else
                        {
                            if (model.Status == Status.Prepare ||
                                model.Status == Status.Activity)
                            {
                                //if (item.name != "")
                                //    model.Name = item.name;
                                if (info.Title != "")
                                    model.ChannelName = info.Title;
                                if (info.Thumbnails.Count > 0)
                                    model.Thumbnail = info.Thumbnails.LastOrDefault()?.Url ?? "";
                                //Console.WriteLine($"[{time}][{index}/{count}] Update jp vtuber {model.Name}");
                                Console.WriteLine($"[{time}][{index}] Update jp vtuber {model.Name}");
                            }
                        }
                        data = new Data
                        {
                            ChannelUrl = model.ChannelUrl,
                            SubscriberCount = info.SubscriberCount,
                            ViewCount = info.ViewCount
                        };
                        _db.Datas.CreateOrUpdate(data);
                        await SleepRandom();
                    }

                    //找下一頁網址
                    var nextChapter = html
                        .Pipe(it => Regex.Match(it, @"href=""([^""]*?)"" >Next"))
                        .Select(m => new
                        {
                            url = m.Groups[1].Value
                        })
                        .Pipe(it => it.url != "" ? it : null);

                    bool isFinish()
                    {
                        if (nextChapter == null)
                            return true;
                        return false;
                    }
                    if (isFinish())
                        break;
                    nextChapterUrl = nextChapter!.url;
                }
            }
        }
    }
}
