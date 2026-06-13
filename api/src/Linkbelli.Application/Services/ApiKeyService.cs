using Linkbelli.Application.Auth;
using Linkbelli.Application.Common;
using Linkbelli.Application.Data;
using Linkbelli.Application.Mapping;
using Linkbelli.Contracts;
using Linkbelli.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Linkbelli.Application.Services;

public class ApiKeyService(IAppDbContext db) : IApiKeyService
{
    public async Task<IReadOnlyList<ApiKeyResponse>> ListAsync(Guid userId, CancellationToken ct = default)
    {
        return await db.ApiKeys
            .Where(k => k.UserId == userId)
            .OrderByDescending(k => k.CreationTime)
            .Select(k => new ApiKeyResponse(
                k.Id, k.Name, k.Prefix, k.Scopes, k.CreationTime, k.LastUsedAt, k.ExpiresAt))
            .ToListAsync(ct);
    }

    public async Task<CreateApiKeyResponse> CreateAsync(Guid userId, CreateApiKeyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ValidationException("name", "Name is required.");
        }

        var generated = ApiKeyToken.Generate();
        var key = new ApiKey
        {
            UserId = userId,
            Name = request.Name.Trim(),
            Prefix = generated.PublicId,
            Hash = generated.Hash,
            Scopes = request.Scopes ?? [],
            ExpiresAt = request.ExpiresAt,
        };
        db.ApiKeys.Add(key);
        await db.SaveChangesAsync(ct);

        return new CreateApiKeyResponse(key.Id, key.Name, key.Prefix, generated.Token, key.Scopes, key.ExpiresAt);
    }

    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id && k.UserId == userId, ct)
                  ?? throw new NotFoundException("API key not found.");

        db.ApiKeys.Remove(key); // soft delete = revocation
        await db.SaveChangesAsync(ct);
    }
}
