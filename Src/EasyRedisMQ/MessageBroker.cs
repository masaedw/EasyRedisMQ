﻿using EasyRedisMQ.Factories;
using EasyRedisMQ.Models;
using EasyRedisMQ.Resolvers;
using EasyRedisMQ.Services;
using StackExchange.Redis.Extensions.Core;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

#if NET462
using System.Runtime.Caching;
#else
using Microsoft.Extensions.Caching.Memory;
#endif

namespace EasyRedisMQ
{
    public class MessageBroker : IMessageBroker
    {
        private IExchangeSubscriberService _exchangeSubscriberService;
        private INotificationService _notificationService;
        private ISubscriberFactory _subscriberFactory;
        private MemoryCache _memoryCache;

        public MessageBroker(IExchangeSubscriberService exchangeSubscriberService, INotificationService notificationService, ISubscriberFactory subscriberFactory)
        {
            _exchangeSubscriberService = exchangeSubscriberService;
            _notificationService = notificationService;
            _subscriberFactory = subscriberFactory;

#if NET462
            _memoryCache = MemoryCache.Default;
#else
            _memoryCache = new MemoryCache(new MemoryCacheOptions());
#endif
        }

        public async Task PublishAsync<T>(T message) where T : class
        {
            var subscriberInfos = await GetSubscriberInfosAsync<T>();

            var tasks = new List<Task>();
            foreach (var subscriberInfo in subscriberInfos)
            { 
                tasks.Add(_exchangeSubscriberService.PushMessageToSubscriberAsync(subscriberInfo, message));
            }
            await Task.WhenAll(tasks);

            await _notificationService.NotifyOfNewMessagesAsync(message);
        }

        public async Task<Subscriber<T>> SubscribeAsync<T>(string subscriberId, Func<T, Task> onMessageAsync) where T : class
        {
            var subScriber = await _subscriberFactory.CreateSubscriberAsync(subscriberId, onMessageAsync);
            await AddSubscriberAsync(subScriber);
            return subScriber;
        }

        private async Task AddSubscriberAsync<T>(Subscriber<T> subScriber) where T : class
        {
            await _exchangeSubscriberService.AddSubscriberAsync(subScriber);
        }

#if NET462
        private async Task<List<SubscriberInfo>> GetSubscriberInfosAsync<T>() where T : class
        {
            var typeName = typeof(T).FullName;
            var cachedSubscriberInfos = _memoryCache[typeName] as List<SubscriberInfo>;
            if (cachedSubscriberInfos != null) return cachedSubscriberInfos;
            var subscriberInfos = await _exchangeSubscriberService.GetSubscriberInfosAsync<T>();
            _memoryCache.Add(typeName, subscriberInfos, DateTimeOffset.Now.AddSeconds(15));
            return subscriberInfos;
        }
#else
        private async Task<List<SubscriberInfo>> GetSubscriberInfosAsync<T>() where T : class
        {
            var typeName = typeof(T).FullName;

            var subscriberInfos = new List<SubscriberInfo>();

            // Look for cache key.
            if (!_memoryCache.TryGetValue(typeName, out subscriberInfos))
            {
                subscriberInfos = await _exchangeSubscriberService.GetSubscriberInfosAsync<T>();
                _memoryCache.Set(typeName, subscriberInfos, DateTimeOffset.Now.AddSeconds(15));
            }
            return subscriberInfos;
        }
#endif
    }
}
