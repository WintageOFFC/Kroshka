namespace Kroshka;
internal sealed class BufferedGrid : DataGridView
{
    public BufferedGrid() { DoubleBuffered = true; }
}
