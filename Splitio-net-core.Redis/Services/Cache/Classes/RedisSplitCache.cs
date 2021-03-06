﻿using Newtonsoft.Json;
using Splitio.Domain;
using Splitio.Redis.Services.Cache.Interfaces;
using Splitio.Services.Cache.Interfaces;
using Splitio.Services.Parsing.Interfaces;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Linq;

namespace Splitio.Redis.Services.Cache.Classes
{
    public class RedisSplitCache : RedisCacheBase, ISplitCache
    {
        private const string splitKeyPrefix = "split.";
        private const string splitsKeyPrefix = "splits.";

        private readonly ISplitParser _splitParser;

        public RedisSplitCache(IRedisAdapter redisAdapter,
            ISplitParser splitParser,
            string userPrefix = null) 
            : base(redisAdapter, userPrefix)
        {
            _splitParser = splitParser;
        }

        public long GetChangeNumber()
        {
            var key = $"{RedisKeyPrefix}{splitsKeyPrefix}till";
            var changeNumberString = _redisAdapter.Get(key);
            var result = long.TryParse(changeNumberString, out long changeNumberParsed);
            
            return result ? changeNumberParsed : -1;
        }

        public ParsedSplit GetSplit(string splitName)
        {
            var key = $"{RedisKeyPrefix}{splitKeyPrefix}{splitName}";
            var splitJson = _redisAdapter.Get(key);

            if (string.IsNullOrEmpty(splitJson))
                return null;

            var split = JsonConvert.DeserializeObject<Split>(splitJson);

            return _splitParser.Parse(split);
        }

        public List<ParsedSplit> GetAllSplits()
        {
            var pattern = $"{RedisKeyPrefix}{splitKeyPrefix}*";
            var splitKeys = _redisAdapter.Keys(pattern);
            var splitValues = _redisAdapter.MGet(splitKeys);

            if (splitValues != null && splitValues.Any())
            {
                var splits = splitValues
                    .Where(x => !x.IsNull)
                    .Select(s => _splitParser.Parse(JsonConvert.DeserializeObject<Split>(s)));

                return splits
                    .Where(s => s != null)
                    .ToList();
            }
            
            return new List<ParsedSplit>();
        }

        public List<string> GetKeys()
        {
            var pattern = $"{RedisKeyPrefix}{splitKeyPrefix}*";
            var splitKeys = _redisAdapter.Keys(pattern);
            var result = splitKeys.Select(x => x.ToString()).ToList();

            return result;
        }

        public void Clear()
        {
            return;
        }

        public bool TrafficTypeExists(string trafficType)
        {
            if (string.IsNullOrEmpty(trafficType)) return false;

            var value = _redisAdapter.Get(GetTrafficTypeKey(trafficType));

            var quantity = value ?? "0";

            int.TryParse(quantity, out int quantityInt);

            return quantityInt > 0;
        }

        private string GetTrafficTypeKey(string type)
        {
            return $"{RedisKeyPrefix}trafficType.{type}";
        }

        public void AddSplit(string splitName, SplitBase split)
        {
            throw new System.NotImplementedException();
        }

        public bool RemoveSplit(string splitName)
        {
            throw new System.NotImplementedException();
        }

        public void SetChangeNumber(long changeNumber)
        {
            throw new System.NotImplementedException();
        }

        public bool AddOrUpdate(string splitName, SplitBase split)
        {
            throw new System.NotImplementedException();
        }

        public long RemoveSplits(List<string> splitNames)
        {
            throw new System.NotImplementedException();
        }

        public void Flush()
        {
            throw new System.NotImplementedException();
        }

        public List<ParsedSplit> FetchMany(List<string> splitNames)
        {
            if (!splitNames.Any()) return new List<ParsedSplit>();

            var redisKey = new List<RedisKey>();

            foreach (var name in splitNames)
            {
                redisKey.Add($"{RedisKeyPrefix}{splitKeyPrefix}{name}");
            }
                        
            var splitValues = _redisAdapter.MGet(redisKey.ToArray());

            if (splitValues == null || !splitValues.Any()) return new List<ParsedSplit>();

            var splits = splitValues
                .Where(s => !s.IsNull)
                .Select(s => _splitParser.Parse(JsonConvert.DeserializeObject<Split>(s)));

            return splits
                .Where(s => s != null)
                .ToList();
        }
    }
}
 