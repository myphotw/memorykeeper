using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Models;

public sealed class SearchChipItem
{
    public SearchChipItem(MemorySearchChipDto chip)
    {
        Label = chip.Label;
        Kind = chip.Kind;
    }

    public string Label { get; }

    public MemorySearchChipKind Kind { get; }

    public string DisplayLabel => Kind switch
    {
        MemorySearchChipKind.Favorite => $"★ {Label}",
        MemorySearchChipKind.Year => Label,
        _ => Label
    };
}
