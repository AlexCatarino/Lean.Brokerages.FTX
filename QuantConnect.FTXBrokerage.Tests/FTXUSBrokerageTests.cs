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

using Moq;
using NUnit.Framework;
using QuantConnect.Configuration;
using QuantConnect.Interfaces;
using QuantConnect.Lean.Engine.DataFeeds;
using QuantConnect.Orders;
using QuantConnect.Packets;
using QuantConnect.Securities;
using QuantConnect.Tests.Brokerages;
using System.Collections.Generic;

namespace QuantConnect.FTXBrokerage.Tests
{
    [TestFixture]
    [Explicit("This test requires a configured and testable FTX.US practice account")]
    public partial class FTXUSBrokerageTests : FTXBrokerageTests
    {
        private static readonly Symbol SUSHI_USD = Symbol.Create("SUSHIUSD", SecurityType.Crypto, Market.FTXUS);

        protected override Symbol Symbol => SUSHI_USD;

        protected override IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider)
            => CreateBrokerage(orderProvider, securityProvider, new LiveNodePacket());

        private IBrokerage CreateBrokerage(IOrderProvider orderProvider, ISecurityProvider securityProvider, LiveNodePacket liveNodePacket)
        {
            ((SecurityProvider)securityProvider)[Symbol] = CreateSecurity(Symbol);

            var apiKey = Config.Get("ftxus-api-key");
            var apiSecret = Config.Get("ftxus-api-secret");
            var accountTier = Config.Get("ftxus-account-tier");

            return new FTXUSBrokerage(
                apiKey,
                apiSecret,
                accountTier,
                orderProvider,
                securityProvider,
                new AggregationManager(),
                liveNodePacket
            );
        }

        /// <summary>
        /// Provides the data required to test each order type in various cases
        /// </summary>
        private static TestCaseData[] OrderParameters()
        {
            return new[]
            {
                new TestCaseData(new MarketOrderTestParameters(SUSHI_USD)).SetName("MarketOrder"),
                new TestCaseData(new NonUpdateableLimitOrderTestParameters(SUSHI_USD, 6m, 5m)).SetName("LimitOrder"),
                new TestCaseData(new NonUpdateableStopMarketOrderTestParameters(SUSHI_USD, 8m, 5m)).SetName("StopMarketOrder"),
                new TestCaseData(new NonUpdateableStopLimitOrderTestParameters(SUSHI_USD, 8m, 5m)).SetName("StopLimitOrder")
            };
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CancelOrders(OrderTestParameters parameters)
        {
            base.CancelOrders(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromZero(OrderTestParameters parameters)
        {
            base.LongFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromLong(OrderTestParameters parameters)
        {
            base.CloseFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromZero(OrderTestParameters parameters)
        {
            base.ShortFromZero(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void CloseFromShort(OrderTestParameters parameters)
        {
            base.CloseFromShort(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void ShortFromLong(OrderTestParameters parameters)
        {
            base.ShortFromLong(parameters);
        }

        [Test, TestCaseSource(nameof(OrderParameters))]
        public override void LongFromShort(OrderTestParameters parameters)
        {
            base.LongFromShort(parameters);
        }
    }
}