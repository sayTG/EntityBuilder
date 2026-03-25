using System.Security.Cryptography;

namespace EntityBuilder.Utilities
{
    public class Cryptography
    {
        public class AesEncryptionManager
        {
            public static string Encrypt(string plainText, string base64Key)
            {
                if (string.IsNullOrEmpty(plainText)) return plainText;

                byte[] key = Convert.FromBase64String(base64Key).Take(32).ToArray();
                byte[] iv;
                byte[] encrypted;

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.GenerateIV();
                    iv = aes.IV;

                    ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                    using (MemoryStream ms = new MemoryStream())
                    {
                        using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                        using (StreamWriter sw = new StreamWriter(cs))
                        {
                            sw.Write(plainText);
                        }
                        encrypted = ms.ToArray();
                    }
                }

                byte[] result = new byte[iv.Length + encrypted.Length];
                Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);

                return Convert.ToBase64String(result);
            }

            public static string Decrypt(string cipherText, string base64Key)
            {
                if (string.IsNullOrEmpty(cipherText)) return cipherText;

                byte[] fullCipher = Convert.FromBase64String(cipherText);
                byte[] key = Convert.FromBase64String(base64Key);

                byte[] iv = new byte[16];
                byte[] cipher = new byte[fullCipher.Length - 16];

                Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
                Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    using (MemoryStream ms = new MemoryStream(cipher))
                    using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                    using (StreamReader sr = new StreamReader(cs))
                    {
                        return sr.ReadToEnd();
                    }
                }
            }
        }
    }
}
