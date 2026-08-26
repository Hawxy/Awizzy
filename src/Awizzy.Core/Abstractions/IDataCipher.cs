namespace Awizzy.Core.Abstractions;

/// <summary>Encrypts data at rest for the current OS user. Windows uses DPAPI; other platforms get their own implementation.</summary>
public interface IDataCipher
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
