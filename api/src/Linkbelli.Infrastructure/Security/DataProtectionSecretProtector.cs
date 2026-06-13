using Linkbelli.Application.Security;
using Microsoft.AspNetCore.DataProtection;

namespace Linkbelli.Infrastructure.Security;

/// <summary>
/// Protects source-config secrets with ASP.NET Core Data Protection. Keys are managed by the
/// host's key ring (persisted to the configured key store); rotate keys there, not here.
/// </summary>
public sealed class DataProtectionSecretProtector : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("Linkbelli.Sources.Secrets.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
