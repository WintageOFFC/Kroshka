using System.ComponentModel;

namespace Kroshka;

// Keep byte counts as Int64 values. Human-readable units are only presentation.
internal sealed class HistoryTable
{
    // The journal stores growth of occupied bytes; every UI shows its effect on free space.
    public static long FreeSpaceEffect(long occupiedGrowth) => checked(-occupiedGrowth);
    readonly Dictionary<int, (string Column, ListSortDirection Direction)> sorts = new();

    public static void AttachFormatting(DataGridView table)
    {
        table.CellFormatting += (_, e) =>
        {
            string column = table.Columns[e.ColumnIndex].Name;
            if (IsSize(column) && e.Value is long bytes)
            {
                e.Value = Theme.Bytes(bytes, column == "size");
                e.FormattingApplied = true;
            }
        };
    }

    static bool IsSize(string column) => column is "size" or "page" or "vss" or "ram" or "before" or "after";

    public void Remember(DataGridView table, int view)
    {
        if (view >= 0 && table.SortedColumn is { } column && table.SortOrder != SortOrder.None)
            sorts[view] = (column.Name, table.SortOrder == SortOrder.Descending
                ? ListSortDirection.Descending : ListSortDirection.Ascending);
    }

    public void Restore(DataGridView table, int view)
    {
        foreach (DataGridViewColumn column in table.Columns)
        {
            if (!IsSize(column.Name)) continue;
            column.ValueType = typeof(long);
            column.DefaultCellStyle.NullValue = column.Name == "page" ? "Нет доступа" : "Нет замера";
        }
        if (sorts.TryGetValue(view, out var sort) && table.Columns.Contains(sort.Column))
            table.Sort(table.Columns[sort.Column], sort.Direction);
    }
}
