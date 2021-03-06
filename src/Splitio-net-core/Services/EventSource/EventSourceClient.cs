﻿using Newtonsoft.Json;
using Splitio.Services.Logger;
using Splitio.Services.Shared.Classes;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Splitio.Services.EventSource
{
    public class EventSourceClient : IEventSourceClient
    {
        private const string KeepAliveResponse = "\n";

        private readonly ISplitLogger _log;
        private readonly Uri _uri;
        private readonly int _readTimeout;

        private readonly object _statusLock = new object();
        private Status _status;

        private HttpClient _httpClient;
        private CancellationTokenSource _cancellationTokenSource;

        public EventSourceClient(string url,
            int readTimeout = 300000,
            ISplitLogger log = null)
        {
            _uri = new Uri(url);
            _readTimeout = readTimeout;
            _log = log ?? WrapperAdapter.GetLogger(typeof(EventSourceClient));

            Task.Factory.StartNew(() => ConnectAsync());
        }

        public event EventHandler<EventReceivedEventArgs> EventReceived;
        public event EventHandler<ErrorReceivedEventArgs> ErrorReceived;

        #region Public Methods
        public Status Status()
        {
            lock (_statusLock)
            {
                return _status;
            }
        }

        public void Disconnect()
        {
            _log.Debug($"Disconnecting from {_uri}");

            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _httpClient.CancelPendingRequests();
            _httpClient.Dispose();

            UpdateStatus(EventSource.Status.Disconnected);

            _log.Info($"Disconnected from {_uri}");
        }
        #endregion

        #region Private Methods
        private async Task ConnectAsync()
        {
            try
            {
                _httpClient = new HttpClient();
                _cancellationTokenSource = new CancellationTokenSource();

                _log.Info($"Connecting to {_uri}");
                UpdateStatus(EventSource.Status.Connecting);

                var request = new HttpRequestMessage(HttpMethod.Get, _uri);

                using (var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token))
                {
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        stream.ReadTimeout = _readTimeout;
                        _log.Info($"Connected to {_uri}");
                        UpdateStatus(EventSource.Status.Connected);
                        await ReadStreamAsync(stream);
                    }
                }
            }
            catch (Exception ex)
            {
                DispatchError(ex.Message);
                UpdateStatus(EventSource.Status.Disconnected);
            }
        }

        private async Task ReadStreamAsync(Stream stream)
        {
            var encoder = new UTF8Encoding();

            _log.Debug($"Reading stream ....");

            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                if (stream.CanRead)
                {
                    var buffer = new byte[2048];

                    int len = await stream.ReadAsync(buffer, 0, 2048, _cancellationTokenSource.Token);

                    if (len > 0 && Status() == EventSource.Status.Connected)
                    {
                        var text = encoder.GetString(buffer, 0, len);
                        _log.Debug($"Read stream encoder buffer: {text}");

                        if (text != KeepAliveResponse)
                        {
                            try
                            {
                                var notification = JsonConvert.DeserializeObject<Notification>(text);
                                var dataJsonString = JsonConvert.SerializeObject(notification.Data.Data);
                                var data = JsonConvert.DeserializeObject<EventData>(dataJsonString);

                                EventData eventData = null;

                                switch (data.Type)
                                {
                                    case NotificationType.SPLIT_UPDATE:
                                        eventData = JsonConvert.DeserializeObject<SplitUpdateEventData>(dataJsonString);
                                        break;
                                    case NotificationType.SPLIT_KILL:
                                        eventData = JsonConvert.DeserializeObject<SplitKillEventData>(dataJsonString);
                                        break;
                                    case NotificationType.SEGMENT_UPDATE:
                                        eventData = JsonConvert.DeserializeObject<SegmentUpdateEventData>(dataJsonString);
                                        break;
                                    case NotificationType.CONTROL:
                                        eventData = JsonConvert.DeserializeObject<ControlEventData>(dataJsonString);
                                        break;
                                    default:
                                        throw new Exception("Unexpected type received from EventSource");
                                }

                                if (eventData == null) throw new Exception("Incorrect format.");

                                DispatchEvent(eventData);
                            }
                            catch (Exception ex)
                            {
                                DispatchError(ex.Message);
                            }
                        }
                    }
                }
            }

            _log.Debug($"Stop read stream");
        }

        private void DispatchEvent(EventData eventData)
        {
            _log.Debug($"DispatchEvent: {eventData}");
            OnEvent(new EventReceivedEventArgs(eventData));
        }

        private void DispatchError(string message)
        {
            _log.Debug($"DispatchError: {message}");
            OnError(new ErrorReceivedEventArgs(message));
        }

        private void OnEvent(EventReceivedEventArgs e)
        {
            EventReceived?.Invoke(this, e);
        }

        private void OnError(ErrorReceivedEventArgs e)
        {
            ErrorReceived?.Invoke(this, e);
        }

        private void UpdateStatus(Status status)
        {
            lock (_statusLock)
            {
                _status = status;
            }
        }
        #endregion
    }
}
