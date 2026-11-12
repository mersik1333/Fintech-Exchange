using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using StockExchange.Models;

namespace StockExchange.Services
{
    public class MarketService : BackgroundService
    {
        private const decimal StartCapital = 100_000m;
        private readonly object _lock = new object();
        private readonly List<Stock> _stocks;
        private readonly Dictionary<string, Holding> _holdings = new Dictionary<string, Holding>();
        private readonly List<Trade> _history = new List<Trade>();

        // Теперь это история только стоимости портфеля
        private readonly List<decimal> _portfolioValueHistory = new List<decimal>();

        private decimal _cash = StartCapital;
        private readonly Random _rnd = new Random();

        public MarketService()
        {
            _stocks = new List<Stock>
            {
                new Stock { Symbol = "GAZP", Name = "Газпром",   Price = 165.40m, Volatility = 0.018 },
                new Stock { Symbol = "SBER", Name = "Сбербанк",  Price = 280.10m, Volatility = 0.015 },
                new Stock { Symbol = "LKOH", Name = "Лукойл",    Price = 6800m,   Volatility = 0.022 },
                new Stock { Symbol = "YNDX", Name = "Яндекс",    Price = 4100m,   Volatility = 0.028 },
                new Stock { Symbol = "AAPL", Name = "Apple",     Price = 190m,    Volatility = 0.020 },
                new Stock { Symbol = "TSLA", Name = "Tesla",     Price = 250m,    Volatility = 0.040 },
                new Stock { Symbol = "NVDA", Name = "Nvidia",    Price = 890m,    Volatility = 0.032 }
            };

            foreach (var s in _stocks)
            {
                s.PrevPrice = s.Price;
                for (int i = 0; i < 10; i++) s.History.Add(s.Price);
            }

            // На старте портфель пуст, поэтому стоимость = 0
            for (int i = 0; i < 10; i++) _portfolioValueHistory.Add(0m);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                lock (_lock)
                {
                    foreach (var s in _stocks)
                    {
                        double change = (_rnd.NextDouble() * 2 - 1) * s.Volatility;
                        s.PrevPrice = s.Price;
                        var next = s.Price * (decimal)(1 + change);
                        next = Math.Clamp(next, s.Price * 0.7m, s.Price * 1.3m);
                        s.Price = Math.Max(1m, Math.Round(next, 2));

                        s.History.Add(s.Price);
                        if (s.History.Count > 50) s.History.RemoveAt(0);
                    }

                    UpdatePortfolioHistory(); // Обновляем историю портфеля
                }
                try { await Task.Delay(2000, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        // Считаем стоимость ТОЛЬКО купленных акций
        private void UpdatePortfolioHistory()
        {
            decimal portfolioValue = _holdings.Where(kv => kv.Value.Qty > 0).Sum(kv => {
                var st = _stocks.FirstOrDefault(s => s.Symbol == kv.Key);
                return st != null ? st.Price * kv.Value.Qty : 0;
            });

            _portfolioValueHistory.Add(portfolioValue);
            if (_portfolioValueHistory.Count > 50) _portfolioValueHistory.RemoveAt(0);
        }

        public object GetState()
        {
            lock (_lock)
            {
                var holdings = _holdings
                    .Where(kv => kv.Value.Qty > 0)
                    .Select(kv =>
                    {
                        var st = _stocks.First(s => s.Symbol == kv.Key);
                        var value = st.Price * kv.Value.Qty;
                        return new
                        {
                            symbol = kv.Key,
                            name = st.Name,
                            quantity = kv.Value.Qty,
                            price = st.Price,
                            value = value,
                            cost = kv.Value.CostBasis,
                            profit = value - kv.Value.CostBasis
                        };
                    })
                    .ToList();

                decimal portfolioValue = holdings.Sum(h => h.value);
                decimal total = _cash + portfolioValue;

                return new
                {
                    cash = _cash,
                    portfolioValue = portfolioValue,
                    total = total,
                    startCapital = StartCapital,
                    profit = total - StartCapital,
                    stocks = _stocks.Select(s => new
                    {
                        symbol = s.Symbol,
                        name = s.Name,
                        price = s.Price,
                        prevPrice = s.PrevPrice,
                        history = s.History
                    }),
                    holdings = holdings,
                    history = _history.TakeLast(15).Reverse().Select(t => new
                    {
                        symbol = t.Symbol,
                        qty = t.Qty,
                        price = t.Price,
                        side = t.Side,
                        time = t.Time.ToString("HH:mm:ss")
                    }),
                    portfolioValueHistory = _portfolioValueHistory // Отправляем новую историю
                };
            }
        }

        public (bool ok, string? error) Buy(string symbol, int qty)
        {
            lock (_lock)
            {
                var st = _stocks.FirstOrDefault(s => s.Symbol == symbol);
                if (st == null) return (false, "Акция не найдена");
                if (qty <= 0) return (false, "Неверное количество");

                var cost = st.Price * qty;
                if (cost > _cash) return (false, "Недостаточно средств");

                _cash -= cost;
                if (!_holdings.TryGetValue(symbol, out var h))
                {
                    h = new Holding();
                    _holdings[symbol] = h;
                }

                h.Qty += qty;
                h.CostBasis += cost;
                _history.Add(new Trade(symbol, qty, st.Price, "BUY", DateTime.Now));

                UpdatePortfolioHistory();
                return (true, null);
            }
        }

        public (bool ok, string? error) Sell(string symbol, int qty)
        {
            lock (_lock)
            {
                var st = _stocks.FirstOrDefault(s => s.Symbol == symbol);
                if (st == null) return (false, "Акция не найдена");
                if (qty <= 0) return (false, "Неверное количество");

                if (!_holdings.TryGetValue(symbol, out var h) || h.Qty < qty)
                    return (false, "Недостаточно акций в портфеле");

                var avgCost = h.CostBasis / h.Qty;
                var revenue = st.Price * qty;

                h.Qty -= qty;
                h.CostBasis -= avgCost * qty;

                if (h.Qty == 0)
                {
                    h.CostBasis = 0;
                    _holdings.Remove(symbol);
                }

                _cash += revenue;
                _history.Add(new Trade(symbol, qty, st.Price, "SELL", DateTime.Now));

                UpdatePortfolioHistory();
                return (true, null);
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _cash = StartCapital;
                _holdings.Clear();
                _history.Clear();
                _portfolioValueHistory.Clear();
                for (int i = 0; i < 10; i++) _portfolioValueHistory.Add(0m);
            }
        }
    }
}