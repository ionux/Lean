/*
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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Indicators;
using QuantConnect.Securities;

namespace QuantConnect.Algorithm.Framework.Selection
{
    /// <summary>
    /// Provides an implementation of <see cref="IUniverseSelectionModel"/> that simply
    /// subscribes to the specified set of symbols
    /// </summary>
    public class EmaCrossUniverseSelectionModel : IUniverseSelectionModel
    {
        private readonly int _fastPeriod;
        private readonly int _slowPeriod;
        private readonly int _universeCount;
        private readonly UniverseSettings _universeSettings;
        private readonly ISecurityInitializer _securityInitializer;
        private const decimal _tolerance = 0.01m;

        // holds our coarse fundamental indicators by symbol
        private readonly ConcurrentDictionary<Symbol, SelectionData> _averages;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualUniverseSelectionModel"/> class
        /// </summary>
        /// <param name="symbols">The symbols to subscribe to</param>
        /// <param name="universeSettings">The settings used when adding symbols to the algorithm, specify null to use algorthm.UniverseSettings</param>
        /// <param name="securityInitializer">Optional security initializer invoked when creating new securities, specify null to use algorithm.SecurityInitializer</param>
        public EmaCrossUniverseSelectionModel(
            int fastPeriod = 12,
            int slowPeriod = 26,
            int universeCount = int.MaxValue,
            UniverseSettings universeSettings = null, 
            ISecurityInitializer securityInitializer = null)
        {
            _fastPeriod = fastPeriod;
            _slowPeriod = slowPeriod;
            _universeCount = universeCount;
            _universeSettings = universeSettings;
            _securityInitializer = securityInitializer;
            _averages = new ConcurrentDictionary<Symbol, SelectionData>();
        }

        /// <summary>
        /// Creates the universes for this algorithm.
        /// Called at algorithm start.
        /// </summary>
        /// <returns>The universes defined by this model</returns>
        public IEnumerable<Universe> CreateUniverses(QCAlgorithmFramework algorithm)
        {
            var universeSettings = _universeSettings ?? algorithm.UniverseSettings;
            var securityInitializer = _securityInitializer ?? algorithm.SecurityInitializer;
            var resolution = universeSettings.Resolution;

            yield return new CoarseFundamentalUniverse(universeSettings, securityInitializer, coarse =>
            {
                return (from cf in coarse
                            // grab th SelectionData instance for this symbol
                            let avg = _averages.GetOrAdd(cf.Symbol, sym => new SelectionData(_fastPeriod, _slowPeriod))
                            // Update returns true when the indicators are ready, so don't accept until they are
                            where avg.Update(cf.EndTime, cf.Price)
                            // only pick symbols who have their 50 day ema over their 100 day ema
                            where avg.Fast > avg.Slow * (1 + _tolerance)
                            // prefer symbols with a larger delta by percentage between the two averages
                            orderby avg.ScaledDelta descending
                            // we only need to return the symbol and return 'Count' symbols
                            select cf.Symbol).Take(_universeCount);
            });
        }

        // class used to improve readability of the coarse selection function
        private class SelectionData
        {
            public readonly ExponentialMovingAverage Fast;
            public readonly ExponentialMovingAverage Slow;

            public SelectionData(int fastPeriod, int slowPeriod)
            {
                Fast = new ExponentialMovingAverage(fastPeriod);
                Slow = new ExponentialMovingAverage(slowPeriod);
            }

            // computes an object score of how much large the fast is than the slow
            public decimal ScaledDelta => (Fast - Slow) / ((Fast + Slow) / 2m);

            // updates the EMAFast and EMASlow indicators, returning true when they're both ready
            public bool Update(DateTime time, decimal value) => Fast.Update(time, value) && Slow.Update(time, value);
        }
    }
}