using System;
using System.Collections.Generic;

namespace StockExchange.Models
{
    public class Stock
    {
        public string Symbol { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Price { get; set; }
        public decimal PrevPrice { get; set; }
        public double Volatility { get; set; } = 0.02;

        // История цен для графика
        public List<decimal> History { get; set; } = new List<decimal>();
    }

    public class Holding
    {
        public int Qty { get; set; }
        public decimal CostBasis { get; set; } // сумма затрат
    }

    public class Trade
    {
        public string Symbol { get; set; }
        public int Qty { get; set; }
        public decimal Price { get; set; }
        public string Side { get; set; }
        public DateTime Time { get; set; }

        public Trade(string symbol, int qty, decimal price, string side, DateTime time)
        {
            Symbol = symbol;
            Qty = qty;
            Price = price;
            Side = side;
            Time = time;
        }
    }
}