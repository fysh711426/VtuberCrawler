using Newtonsoft.Json;
using VtuberCrawler.Models;
using VtuberCrawler.Storages;
using VtuberCrawler.Extensions;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Serialization;

namespace VtuberCrawler.Api
{
    public class JsonDataCreator
    {
        private string _path;
        private string _vtuberPath;
        private DateTime _today;
        private DbContext _db = new DbContext();
        private DbContext? _dbLastDay7 = null;
        private DbContext? _dbLastDay14 = null;
        private DbContext? _dbLastDay30 = null;
        //private DbContext? _dbLastDay60 = null;
        public JsonDataCreator(string path, string vtuberPath, DateTime today)
        {
            _path = path;
            _vtuberPath = vtuberPath;
            _today = today;
        }

        private static readonly JsonSerializerSettings _jsonSettings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented
        };

        private static readonly List<(string name,
            Func<Vtuber, bool> predicate)> _actions = new() 
        {
            ("tw", (it) => it.Area == "TW"),
            ("hololive", (it) => it.Company == "Hololive"),
            ("anycolor", (it) => it.Company == "彩虹社"),
            ("other", (it) => it.Area != "TW" && it.Company != "Hololive" && it.Company != "彩虹社")
        };

        private static int? _getRankVar(string key, int rank,
            Dictionary<string, int>? rankLast) =>
                rankLast == null ? null :
                    !rankLast.ContainsKey(key) ? null :
                        rankLast[key] - rank;

        public void Init(List<string> csvList)
        {
            _db = new DbContext();
            _db.Vtubers = new(_vtuberPath, it => it.ChannelUrl);
            _db.Datas = new(csvList.First(), it => it.ChannelUrl);

            var csvDateList = csvList
                .Select(it => new
                {
                    path = it,
                    date = DateTime.Parse(
                        Regex.Match(it, @"([\d]{4}-[\d]{2}-[\d]{2})")
                            .Groups[1].Value).Date
                });

            var lastDay7 = csvDateList
                .Where(it => (_today - it.date).Days >= 7)
                .OrderByDescending(it => it.path)
                .FirstOrDefault()?
                .path;

            _dbLastDay7 = null;
            if (lastDay7 != null)
            {
                _dbLastDay7 = new DbContext();
                _dbLastDay7.Datas = new(lastDay7, it => it.ChannelUrl);
            }

            var lastDay14 = csvDateList
                .Where(it => (_today - it.date).Days >= 14)
                .OrderByDescending(it => it.path)
                .FirstOrDefault()?
                .path;

            _dbLastDay14 = null;
            if (lastDay14 != null)
            {
                _dbLastDay14 = new DbContext();
                _dbLastDay14.Datas = new(lastDay14, it => it.ChannelUrl);
            }

            var lastDay30 = csvDateList
                .Where(it => (_today - it.date).Days >= 30)
                .OrderByDescending(it => it.path)
                .FirstOrDefault()?
                .path;

            _dbLastDay30 = null;
            if (lastDay30 != null)
            {
                _dbLastDay30 = new DbContext();
                _dbLastDay30.Datas = new(lastDay30, it => it.ChannelUrl);
            }

            //var lastDay60 = csvDateList
            //    .Where(it => (_today - it.date).Days >= 60)
            //    .OrderByDescending(it => it.path)
            //    .FirstOrDefault()?
            //    .path;

            //_dbLastDay60 = null;
            //if (lastDay60 != null)
            //{
            //    _dbLastDay60 = new DbContext();
            //    _dbLastDay60.Datas = new(lastDay60, it => it.ChannelUrl);
            //}
        }

        public async Task Update()
        {
            await _db.Datas.Load();
            await _db.Vtubers.Load();

            if (_dbLastDay7 != null)
                await _dbLastDay7.Datas.Load();
            if (_dbLastDay14 != null)
                await _dbLastDay14.Datas.Load();
            if (_dbLastDay30 != null)
                await _dbLastDay30.Datas.Load();
            //if (_dbLastDay60 != null)
            //    await _dbLastDay60.Datas.Load();

            var vtuberDict = _db.Vtubers.GetAll()
                .ToDictionary(it => it.ChannelUrl);

            var dataList = _db.Datas.GetAll()
                .Select<Data, (Data data, Vtuber vtuber)>(
                    it => (it, vtuberDict[it.ChannelUrl]))
                .Where(it => it.vtuber.Status == Status.Activity)
                .ToList();

            var lastDay7 = _dbLastDay7?.Datas.GetAll()
                .Select<Data, (Data data, Vtuber vtuber)>(
                    it => (it, vtuberDict[it.ChannelUrl]))
                .ToList();
            var lastDay14 = _dbLastDay14?.Datas.GetAll()
                .Select<Data, (Data data, Vtuber vtuber)>(
                    it => (it, vtuberDict[it.ChannelUrl]))
                .ToList();
            var lastDay30 = _dbLastDay30?.Datas.GetAll()
                .Select<Data, (Data data, Vtuber vtuber)>(
                    it => (it, vtuberDict[it.ChannelUrl]))
                .ToList();
            //var lastDay60 = _dbLastDay60?.Datas.GetAll()
            //    .Select<Data, (Data data, Vtuber vtuber)>(
            //        it => (it, vtuberDict[it.ChannelUrl]))
            //    .ToList();

            UpdateSubscribe(dataList, lastDay7, lastDay30);
            UpdatePopular(dataList, lastDay7, lastDay30);
            UpdateSinging(dataList, lastDay7, lastDay30);
            UpdateGrowing(dataList, lastDay7, lastDay14, lastDay30);
            UpdateNewcomer(dataList, lastDay7, lastDay30);
        }

        private void UpdateSubscribe(List<(Data data, Vtuber vtuber)> dataList,
            List<(Data data, Vtuber vtuber)>? lastDay7, 
            List<(Data data, Vtuber vtuber)>? lastDay30)
        {
            JsonData MapData((Data data, Vtuber vtuber) item, int rank, 
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.SubscriberCount.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewVideoTitleDay30,
                    VideoViewCount = item.data.HighestViewCountDay30.GetCountText(),
                    VideoThumbnail = item.data.HighestViewVideoThumbnailDay30,
                    VideoUrl = item.data.HighestViewVideoUrlDay30,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            foreach(var action in _actions)
            {
                var rankDay30 = lastDay30?
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it => it.data)
                    .RankDesc(it => it.SubscriberCount)
                    .Select(it => (it.Rank, it.Item.ChannelUrl))
                    .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                var datas = dataList
                    .Where(it => action.predicate(it.vtuber))
                    .RankDesc(it => it.data.SubscriberCount)
                    .Select(it => MapData(it.Item, it.Rank, rankDay30))
                    .BreakOn(it => it.Rank > 100);
                var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                File.WriteAllText(Path.Combine(_path, $"subscribe_{action.name}.json"), json);
            }
        }

        private void UpdatePopular(List<(Data data, Vtuber vtuber)> dataList,
            List<(Data data, Vtuber vtuber)>? lastDay7, 
            List<(Data data, Vtuber vtuber)>? lastDay30)
        {
            JsonData MapDataDay7((Data data, Vtuber vtuber) item, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.MedianViewCountDay7.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewVideoTitleDay7,
                    VideoViewCount = item.data.HighestViewCountDay7.GetCountText(),
                    VideoThumbnail = item.data.HighestViewVideoThumbnailDay7,
                    VideoUrl = item.data.HighestViewVideoUrlDay7,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            JsonData MapDataDay30((Data data, Vtuber vtuber) item, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.MedianViewCountDay30.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewVideoTitleDay30,
                    VideoViewCount = item.data.HighestViewCountDay30.GetCountText(),
                    VideoThumbnail = item.data.HighestViewVideoThumbnailDay30,
                    VideoUrl = item.data.HighestViewVideoUrlDay30,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            foreach (var action in _actions)
            {
                // Day7
                {
                    var rankDay7 = lastDay7?
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.MedianViewCountDay7 > 0)
                        .Select(it => it.data)
                        .RankDesc(it => it.MedianViewCountDay7)
                        .Select(it => (it.Rank, it.Item.ChannelUrl))
                        .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                    var datas = dataList
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.MedianViewCountDay7 > 0)
                        .RankDesc(it => it.data.MedianViewCountDay7)
                        .Select(it => MapDataDay7(it.Item, it.Rank, rankDay7))
                        .BreakOn(it => it.Rank > 100);
                    var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                    File.WriteAllText(Path.Combine(_path, $"popular_{action.name}_day7.json"), json);
                }
                
                // Day30
                {
                    var rankDay30 = lastDay30?
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.MedianViewCountDay30 > 0)
                        .Select(it => it.data)
                        .RankDesc(it => it.MedianViewCountDay30)
                        .Select(it => (it.Rank, it.Item.ChannelUrl))
                        .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                    var datas = dataList
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.MedianViewCountDay30 > 0)
                        .RankDesc(it => it.data.MedianViewCountDay30)
                        .Select(it => MapDataDay30(it.Item, it.Rank, rankDay30))
                        .BreakOn(it => it.Rank > 100);
                    var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                    File.WriteAllText(Path.Combine(_path, $"popular_{action.name}_day30.json"), json);
                }
            }
        }

        private void UpdateSinging(List<(Data data, Vtuber vtuber)> dataList,
            List<(Data data, Vtuber vtuber)>? lastDay7, 
            List<(Data data, Vtuber vtuber)>? lastDay30)
        {
            JsonData MapDataDay7((Data data, Vtuber vtuber) item, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.HighestSingingViewCountDay7.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewSingingVideoTitleDay7,
                    VideoViewCount = item.data.HighestSingingViewCountDay7.GetCountText(),
                    VideoThumbnail = item.data.HighestViewSingingVideoThumbnailDay7,
                    VideoUrl = item.data.HighestViewSingingVideoUrlDay7,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            JsonData MapDataDay30((Data data, Vtuber vtuber) item, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.HighestSingingViewCountDay30.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewSingingVideoTitleDay30,
                    VideoViewCount = item.data.HighestSingingViewCountDay30.GetCountText(),
                    VideoThumbnail = item.data.HighestViewSingingVideoThumbnailDay30,
                    VideoUrl = item.data.HighestViewSingingVideoUrlDay30,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            foreach (var action in _actions)
            {
                // Day7
                {
                    var rankDay7 = lastDay7?
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.HighestSingingViewCountDay7 > 0)
                        .Select(it => it.data)
                        .RankDesc(it => it.HighestSingingViewCountDay7)
                        .Select(it => (it.Rank, it.Item.ChannelUrl))
                        .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                    var datas = dataList
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.HighestSingingViewCountDay7 > 0)
                        .RankDesc(it => it.data.HighestSingingViewCountDay7)
                        .Select(it => MapDataDay7(it.Item, it.Rank, rankDay7))
                        .BreakOn(it => it.Rank > 100);
                    var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                    File.WriteAllText(Path.Combine(_path, $"singing_{action.name}_day7.json"), json);
                }

                // Day30
                {
                    var rankDay30 = lastDay30?
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.HighestSingingViewCountDay30 > 0)
                        .Select(it => it.data)
                        .RankDesc(it => it.HighestSingingViewCountDay30)
                        .Select(it => (it.Rank, it.Item.ChannelUrl))
                        .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                    var datas = dataList
                        .Where(it => action.predicate(it.vtuber))
                        .Where(it => it.data.HighestSingingViewCountDay30 > 0)
                        .RankDesc(it => it.data.HighestSingingViewCountDay30)
                        .Select(it => MapDataDay30(it.Item, it.Rank, rankDay30))
                        .BreakOn(it => it.Rank > 100);
                    var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                    File.WriteAllText(Path.Combine(_path, $"singing_{action.name}_day30.json"), json);
                }
            }
        }

        private void UpdateGrowing(List<(Data data, Vtuber vtuber)> dataList,
            List<(Data data, Vtuber vtuber)>? lastDay7,
            List<(Data data, Vtuber vtuber)>? lastDay14,
            List<(Data data, Vtuber vtuber)>? lastDay30)
        {
            JsonData MapData((Data data, Vtuber vtuber) item, double rate, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = rate.ToString("0.##"),
                    VideoTitle = item.data.HighestViewVideoTitleDay30,
                    VideoViewCount = item.data.HighestViewCountDay30.GetCountText(),
                    VideoThumbnail = item.data.HighestViewVideoThumbnailDay30,
                    VideoUrl = item.data.HighestViewVideoUrlDay30,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            foreach (var action in _actions)
            {
                var lastDay7Dict = lastDay7?
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it => it.data)
                    .ToDictionary(it => it.ChannelUrl);

                var lastDay14Dict = lastDay14?
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it => it.data)
                    .ToDictionary(it => it.ChannelUrl);

                var rankDay7 = lastDay7?
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it =>
                    {
                        var key = it.data.ChannelUrl;

                        var lastDay14Item = lastDay14Dict == null ? null :
                            !lastDay14Dict.ContainsKey(key) ? null : lastDay14Dict[key];

                        var rate = lastDay14Item == null ? 0 :
                            lastDay14Item.SubscriberCount == 0 ? 0 :
                                (double)(it.data.SubscriberCount -
                                    lastDay14Item.SubscriberCount) /
                                        lastDay14Item.SubscriberCount * 100;

                        return new
                        {
                            rate = rate,
                            item = it
                        };
                    })
                    .Where(it => it.rate > 0)
                    .RankDesc(it => it.rate)
                    .Select(it => (it.Rank, it.Item.item.data.ChannelUrl))
                    .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                var datas = dataList
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it =>
                    {
                        var key = it.data.ChannelUrl;

                        var lastDay7Item = lastDay7Dict == null ? null :
                            !lastDay7Dict.ContainsKey(key) ? null : lastDay7Dict[key];

                        var rate = lastDay7Item == null ? 0 :
                            lastDay7Item.SubscriberCount == 0 ? 0 :
                                (double)(it.data.SubscriberCount -
                                    lastDay7Item.SubscriberCount) /
                                        lastDay7Item.SubscriberCount * 100;

                        return new
                        {
                            rate = rate,
                            item = it
                        };
                    })
                    .Where(it => it.rate > 0)
                    .RankDesc(it => it.rate)
                    .Select(it => MapData(it.Item.item, 
                        it.Item.rate, it.Rank, rankDay7))
                    .BreakOn(it => it.Rank > 100);
                var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                File.WriteAllText(Path.Combine(_path, $"growing_{action.name}.json"), json);
            }
        }

        private void UpdateNewcomer(List<(Data data, Vtuber vtuber)> dataList,
            List<(Data data, Vtuber vtuber)>? lastDay7, 
            List<(Data data, Vtuber vtuber)>? lastDay30)
        {
            JsonData MapData((Data data, Vtuber vtuber) item, int rank,
                Dictionary<string, int>? rankLast)
            {
                return new JsonData
                {
                    Id = item.data.Id,
                    ChannelUrl = item.data.ChannelUrl,
                    Name = item.vtuber.Name,
                    Thumbnail = item.vtuber.Thumbnail,
                    Subscribe = item.data.SubscriberCount.GetCountText(),
                    Score = item.data.SubscriberCount.GetCountSeparateText(),
                    VideoTitle = item.data.HighestViewVideoTitleDay30,
                    VideoViewCount = item.data.HighestViewCountDay30.GetCountText(),
                    VideoThumbnail = item.data.HighestViewVideoThumbnailDay30,
                    VideoUrl = item.data.HighestViewVideoUrlDay30,
                    Rank = rank,
                    RankVar = _getRankVar(item.data.ChannelUrl, rank, rankLast)
                };
            }

            foreach (var action in _actions)
            {
                var rankDay30 = lastDay30?
                    .Where(it => action.predicate(it.vtuber))
                    .Select(it => it.data)
                    .RankDesc(it => it.SubscriberCount)
                    .Select(it => (it.Rank, it.Item.ChannelUrl))
                    .ToDictionary(it => it.ChannelUrl, it => it.Rank);

                var datas = dataList
                    .Where(it => action.predicate(it.vtuber))
                    .Where(it => (_today - DateTime.Parse(it.vtuber.CreateTime).Date).Days < 30)
                    .RankDesc(it => it.data.SubscriberCount)
                    .Select(it => MapData(it.Item, it.Rank, rankDay30));
                var json = JsonConvert.SerializeObject(datas, _jsonSettings);
                File.WriteAllText(Path.Combine(_path, $"newcomer_{action.name}.json"), json);
            }
        }
    }
}
