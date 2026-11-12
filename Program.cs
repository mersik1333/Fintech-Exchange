using StockExchange.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<MarketService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketService>());

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", (MarketService m) => m.GetState());

app.MapPost("/api/buy", (TradeRequest r, MarketService m) =>
{
    var (ok, error) = m.Buy(r.Symbol, r.Quantity);
    return Results.Json(new { ok, error });
});

app.MapPost("/api/sell", (TradeRequest r, MarketService m) =>
{
    var (ok, error) = m.Sell(r.Symbol, r.Quantity);
    return Results.Json(new { ok, error });
});

app.MapPost("/api/reset", (MarketService m) => { m.Reset(); return Results.Json(new { ok = true }); });

app.Run();

public record TradeRequest(string Symbol, int Quantity);