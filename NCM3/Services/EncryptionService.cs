using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace NCM3.Services
{
    public interface IEncryptionService
    {
        string Encrypt(string text);
        string Decrypt(string cipherText);
    }

    public class EncryptionService : IEncryptionService
    {
        private readonly byte[] _key;
        private readonly byte[] _iv;

        public EncryptionService(IConfiguration configuration)
        {
            var encryptionKey = configuration["EncryptionKey"];
            
            if (string.IsNullOrEmpty(encryptionKey))
            {
                throw new InvalidOperationException("Encryption key is not configured. Please add an EncryptionKey in appsettings.json");
            }

            // Sử dụng key từ cấu hình hoặc tạo key cố định từ chuỗi
            using (var deriveBytes = new Rfc2898DeriveBytes(encryptionKey, Encoding.UTF8.GetBytes("NCM3Salt"), 1000))
            {
                _key = deriveBytes.GetBytes(32); // 256 bits
                _iv = deriveBytes.GetBytes(16);  // 128 bits
            }
        }

        public string Encrypt(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            using (Aes aes = Aes.Create())
            {
                aes.Key = _key;
                aes.IV = _iv;

                ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        using (StreamWriter sw = new StreamWriter(cs))
                        {
                            sw.Write(text);
                        }
                    }
                    return Convert.ToBase64String(ms.ToArray());
                }
            }
        }

        public string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText))
                return cipherText;

            try
            {
                byte[] buffer = Convert.FromBase64String(cipherText);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = _key;
                    aes.IV = _iv;

                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    using (MemoryStream ms = new MemoryStream(buffer))
                    {
                        using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                        {
                            using (StreamReader sr = new StreamReader(cs))
                            {
                                return sr.ReadToEnd();
                            }
                        }
                    }
                }
            }
            catch
            {
                // Nếu không thể giải mã (có thể là dữ liệu không được mã hóa)
                return cipherText;
            }
        }
    }
}
