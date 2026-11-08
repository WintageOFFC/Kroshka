namespace Kroshka;

internal static class TablePages
{
    public static bool EqualRows(List<object?[]> a, List<object?[]> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.SequenceEqual(pair.Second));

    public static List<DataGridViewRow> Sort(IEnumerable<DataGridViewRow> rows, int column, bool descending)
    {
        var comparer = Comparer<object?>.Create((a, b) => {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            if (a is long x && b is long y) return x.CompareTo(y);
            return StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(a), Convert.ToString(b));
        });
        return (descending ? rows.OrderByDescending(r => r.Cells[column].Value, comparer)
            : rows.OrderBy(r => r.Cells[column].Value, comparer)).ToList();
    }

    public static string ExportValue(DataGridViewCell cell, string column) => cell.Value is long n && column is "size" or "page" or "vss" or "ram" or "before" or "after"
        ? Theme.Bytes(n, column == "size") : Convert.ToString(cell.Value) ?? "";
}
