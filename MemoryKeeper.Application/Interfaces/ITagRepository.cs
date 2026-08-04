using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Domain.Enums;

namespace MemoryKeeper.Application.Interfaces;

public interface ITagRepository
{
    Task<Tag?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Tag?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetAllAsync(
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> GetPopularAsync(
        int take = 20,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Tag>> SearchAsync(
        string keyword,
        TagSource? source = TagSource.User,
        CancellationToken cancellationToken = default);

    Task AddAsync(Tag tag, CancellationToken cancellationToken = default);

    Task UpdateAsync(Tag tag, CancellationToken cancellationToken = default);

    Task DeleteAsync(Tag tag, CancellationToken cancellationToken = default);
}
