using DbExplorer.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace DbExplorer.Infrastructure.Security;

public sealed class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("DbExplorer.ConnectionSecrets");

    public string Protect(string plainText) => _protector.Protect(plainText);

    public string Unprotect(string cipherText) => _protector.Unprotect(cipherText);
}
