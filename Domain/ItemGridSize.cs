namespace D2CompanionMvc.Domain;

public sealed class ItemGridSize
{
    public int PixelWidth { get; init; }

    public int PixelHeight { get; init; }

    public int GridWidth { get; init; } = 1;

    public int GridHeight { get; init; } = 1;
}
