namespace Kroshka;

internal readonly record struct HistoryPeriod(long From, long To)
{
    public static HistoryPeriod Select(string preset, DateTime start, DateTime end, long earliest)
    {
        var last = end.Date;
        var first = preset switch
        {
            "1D" => last,
            "1W" => last.AddDays(-6),
            "1M" => last.AddMonths(-1).AddDays(1),
            "3M" => last.AddMonths(-3).AddDays(1),
            "1Y" => last.AddYears(-1).AddDays(1),
            _ => start.Date
        };
        return new(preset == "ALL" ? earliest : new DateTimeOffset(first).ToUnixTimeSeconds(),
            new DateTimeOffset(last.AddDays(1)).ToUnixTimeSeconds());
    }
    public string Caption => Local(From).ToString("dd.MM.yyyy") + " — " + Local(To - 1).ToString("dd.MM.yyyy");
    public static DateTime Local(long timestamp) => DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
}
