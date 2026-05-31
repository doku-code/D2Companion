using System.Text.Json.Serialization;

namespace D2CompanionMvc.Models.Catalog;

public sealed class ItemRecord
{
    public int ItemColor { get; set; } = -1;

    public string Image { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Header { get; set; }

    public List<string> Sockets { get; set; } = [];

    public string Account { get; set; } = string.Empty;

    public string Character { get; set; } = string.Empty;

    public string SourceFile { get; set; } = string.Empty;

    public string? Realm { get; set; }

    public string? Mode { get; set; }

    public bool Hardcore { get; set; }

    public bool Expansion { get; set; }

    public bool Ladder { get; set; }

    public string Gid { get; set; } = string.Empty;

    public int Classid { get; set; }

    public int Location { get; set; }

    public string Storage { get; set; } = string.Empty;

    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public int GridWidth { get; set; } = 1;

    public int GridHeight { get; set; } = 1;

    public bool Ethereal { get; set; }

    public string? Tail { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object>? AdditionalData { get; set; }
}
