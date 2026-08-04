using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.App.Models;

public sealed class SearchSuggestionItem
{
    public SearchSuggestionItem(MemorySearchSuggestionDto suggestion)
    {
        Text = suggestion.Text;
        KindLabel = suggestion.KindLabel;
        Kind = suggestion.Kind;
    }

    public string Text { get; }

    public string KindLabel { get; }

    public MemorySearchSuggestionKind Kind { get; }

    public string DisplayText => $"{Text}  ({KindLabel})";
}
