﻿using DipExecutor.Service;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DipExecutor.Notification
{
    public class ExecutorNotifier : BatchNotifier
    {
        private readonly HttpClient httpClient;

        public ExecutorNotifier(IHttpClientFactory httpClientFactory)
        {
            httpClient = httpClientFactory.GetHttpClient();

            // TODO: get this from config...
            interval = new TimeSpan(0, 0, 1);
            batchSize = null;
            queueSize = null;

            Start();
        }

        public override async Task WriteNotificationAsync(IEnumerable<StepNotification> notifications, CancellationToken cancellationToken)
        {
            var logMessages = notifications.ToList();
            var jsonContent = JsonConvert.SerializeObject(logMessages);
            using (var response = await httpClient.PostAsync(notifications.First<StepNotification>().NotificationUrl, new StringContent(jsonContent, System.Text.Encoding.UTF8, "application/json")))
            {
                var content = await response.Content.ReadAsStringAsync();

                // fire and forget?
            }

        }
    }
}