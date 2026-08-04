using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Infrastructure.Repositories;

public sealed class InMemoryTagRepository : ITagRepository
{
    private readonly List<Tag> _items = [];

    public Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.FirstOrDefault(tag => tag.Id == id));

    public Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        return Task.FromResult(
            _items.FirstOrDefault(tag =>
                string.Equals(tag.Name, normalized, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<IReadOnlyList<Tag>> GetAllAsync(
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Tag> query = _items;
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        return Task.FromResult<IReadOnlyList<Tag>>(
            query.OrderByDescending(tag => tag.UsageCount).ThenBy(tag => tag.Name).ToList());
    }

    public Task<IReadOnlyList<Tag>> GetPopularAsync(
        int take = 20,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Tag> query = _items;
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        return Task.FromResult<IReadOnlyList<Tag>>(
            query.OrderByDescending(tag => tag.UsageCount).ThenBy(tag => tag.Name).Take(take).ToList());
    }

    public Task<IReadOnlyList<Tag>> SearchAsync(
        string keyword,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Tag> query = _items;
        if (source.HasValue)
        {
            query = query.Where(tag => tag.Source == source.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            query = query.Where(tag => tag.Name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult<IReadOnlyList<Tag>>(
            query.OrderByDescending(tag => tag.UsageCount).ThenBy(tag => tag.Name).ToList());
    }

    public Task AddAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        _items.Add(Clone(tag));
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        var index = _items.FindIndex(item => item.Id == tag.Id);
        if (index >= 0)
        {
            _items[index] = Clone(tag);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Tag tag, CancellationToken cancellationToken = default)
    {
        _items.RemoveAll(item => item.Id == tag.Id);
        return Task.CompletedTask;
    }

    private static Tag Clone(Tag tag) => new()
    {
        Id = tag.Id,
        Name = tag.Name,
        Color = tag.Color,
        UsageCount = tag.UsageCount,
        Source = tag.Source,
        IsPinned = tag.IsPinned,
        CreatedAt = tag.CreatedAt,
        UpdatedAt = tag.UpdatedAt
    };
}
