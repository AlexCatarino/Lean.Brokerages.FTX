﻿/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QuantConnect.Brokerages;
using QuantConnect.Data.Market;
using QuantConnect.Logging;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace QuantConnect.FTXBrokerage
{
    public partial class FTXBrokerage
    {
        private ManualResetEvent _onSubscribeEvent = new(false);
        private ManualResetEvent _onUnsubscribeEvent = new(false);
        private readonly ConcurrentDictionary<Symbol, DefaultOrderBook> _orderBooks = new();

        /// <summary>
        /// Locking object for the Ticks list in the data queue handler
        /// </summary>
        protected readonly object TickLocker = new object();

        private bool SubscribeChannel(string channel, Symbol symbol)
        {
            _onSubscribeEvent.Reset();

            WebSocket.Send(JsonConvert.SerializeObject(new
            {
                op = "subscribe",
                channel,
                market = _symbolMapper.GetBrokerageSymbol(symbol)
            }, FTXRestApiClient.JsonSettings));

            if (!_onSubscribeEvent.WaitOne(TimeSpan.FromSeconds(30)))
            {
                Log.Error($"FTXBrokerage.Subscribe(): Could not subscribe to {symbol.Value}/{channel}.");
                return false;
            }

            return true;
        }

        private bool UnsubscribeChannel(string channel, Symbol symbol)
        {
            _onUnsubscribeEvent.Reset();

            WebSocket.Send(JsonConvert.SerializeObject(new
            {
                op = "unsubscribe",
                channel,
                market = _symbolMapper.GetBrokerageSymbol(symbol)
            }, FTXRestApiClient.JsonSettings));

            if (!_onUnsubscribeEvent.WaitOne(TimeSpan.FromSeconds(30)))
            {
                Log.Error($"FTXBrokerage.Unsubscribe(): Could not unsubscribe from {symbol.Value}/{channel}.");
                return false;
            }

            return true;
        }

        private void OnMessageImpl(WebSocketMessage webSocketMessage)
        {
            var e = (WebSocketClientWrapper.TextMessage)webSocketMessage.Data;
            try
            {
                var obj = JsonConvert.DeserializeObject<JObject>(e.Message, FTXRestApiClient.JsonSettings);

                var objEventType = obj["type"];
                switch (objEventType?.ToObject<string>()?.ToLowerInvariant())
                {
                    case "pong":
                        {
                            return;
                        }

                    case "subscribed":
                        {
                            _onSubscribeEvent.Set();
                            return;
                        }

                    case "unsubscribed":
                        {
                            _onUnsubscribeEvent.Set();
                            return;
                        }

                    case "error":
                        {
                            // status code 400 - already subscribed
                            if (obj["msg"]?.ToObject<string>() == "Already subscribed")
                            {
                                _onSubscribeEvent.Set();
                            }
                            return;
                        }

                    case "update":
                        {
                            OnDataUpdate(obj);
                            return;
                        }

                    case "partial":
                        {
                            OnSnapshot(
                                obj["market"]?.ToObject<string>(),
                                obj["data"]?.ToObject<Snapshot>());
                            return;
                        }

                    default:
                        {
                            return;
                        }
                }
            }
            catch (Exception exception)
            {
                OnMessage(new BrokerageMessageEvent(BrokerageMessageType.Error, -1, $"Parsing wss message failed. Data: {e.Message} Exception: {exception}"));
                throw;
            }
        }

        private void OnDataUpdate(JObject obj)
        {
            switch (obj["channel"]?.ToObject<string>()?.ToLowerInvariant())
            {
                case "trades":
                    {
                        OnTrade(
                            obj.SelectToken("market")?.ToObject<string>(),
                            obj.SelectToken("data")?.ToObject<Trade[]>());
                        return;
                    }
                case "orderbook":
                    {
                        OnOrderbookUpdate(
                            obj.SelectToken("market")?.ToObject<string>(),
                            obj.SelectToken("data")?.ToObject<OrderbookUpdate>());
                        return;
                    }
            }
        }

        private void OnTrade(string market, Trade[] trades)
        {
            try
            {
                var securityType = _symbolMapper.GetBrokerageSecurityType(market);
                var symbol = _symbolMapper.GetLeanSymbol(market, securityType, Market.FTX);
                foreach (var trade in trades)
                {
                    EmitTradeTick(
                        symbol,
                        trade.Time,
                        trade.Price,
                        trade.Quantity);
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnSnapshot(string market, Snapshot snapshot)
        {
            try
            {
                var securityType = _symbolMapper.GetBrokerageSecurityType(market);
                var symbol = _symbolMapper.GetLeanSymbol(market, securityType, Market.FTX);

                DefaultOrderBook orderBook;
                if (!_orderBooks.TryGetValue(symbol, out orderBook))
                {
                    orderBook = new DefaultOrderBook(symbol);
                    _orderBooks[symbol] = orderBook;
                }
                else
                {
                    orderBook.BestBidAskUpdated -= OnBestBidAskUpdated;
                    orderBook.Clear();
                }

                foreach (var row in snapshot.Bids)
                {
                    orderBook.UpdateBidRow(row[0], row[1]);
                }
                foreach (var row in snapshot.Asks)
                {
                    orderBook.UpdateAskRow(row[0], row[1]);
                }

                orderBook.BestBidAskUpdated += OnBestBidAskUpdated;

                EmitQuoteTick(symbol, orderBook.BestBidPrice, orderBook.BestBidSize, orderBook.BestAskPrice, orderBook.BestAskSize);
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnOrderbookUpdate(string market, OrderbookUpdate update)
        {
            try
            {
                var securityType = _symbolMapper.GetBrokerageSecurityType(market);
                var symbol = _symbolMapper.GetLeanSymbol(market, securityType, Market.FTX);

                if (!_orderBooks.TryGetValue(symbol, out var orderBook))
                {
                    throw new Exception($"FTXBRokerage.OnOrderbookUpdate: orderbook is not initialized for {market}.");
                }

                foreach (var row in update.Bids)
                {
                    if (row[1] == 0)
                    {
                        orderBook.RemoveBidRow(row[0]);
                        continue;
                    }

                    orderBook.UpdateBidRow(row[0], row[1]);
                }
                foreach (var row in update.Asks)
                {
                    if (row[1] == 0)
                    {
                        orderBook.RemoveAskRow(row[0]);
                        continue;
                    }

                    orderBook.UpdateAskRow(row[0], row[1]);
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void EmitTradeTick(Symbol symbol, DateTime time, decimal price, decimal quantity)
        {
            try
            {
                lock (TickLocker)
                {
                    EmitTick(new Tick
                    {
                        Value = price,
                        Time = time,
                        Symbol = symbol,
                        TickType = TickType.Trade,
                        Quantity = Math.Abs(quantity)
                    });
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        private void OnBestBidAskUpdated(object sender, BestBidAskUpdatedEventArgs e)
        {
            EmitQuoteTick(e.Symbol, e.BestBidPrice, e.BestBidSize, e.BestAskPrice, e.BestAskSize);
        }

        private void EmitQuoteTick(Symbol symbol, decimal bidPrice, decimal bidSize, decimal askPrice, decimal askSize)
        {
            try
            {
                lock (TickLocker)
                {
                    EmitTick(new Tick
                    {
                        AskPrice = askPrice,
                        BidPrice = bidPrice,
                        Time = DateTime.UtcNow,
                        Symbol = symbol,
                        TickType = TickType.Quote,
                        AskSize = askSize,
                        BidSize = bidSize
                    });
                }
            }
            catch (Exception e)
            {
                Log.Error(e);
                throw;
            }
        }

        /// <summary>
        /// Emit stream tick
        /// </summary>
        /// <param name="tick"></param>
        private void EmitTick(Tick tick)
        {
            _aggregator.Update(tick);
        }
    }
}
