using System.Security.Cryptography;
using System.Text;

using McsProto;

using RustPlusApi.Fcm.Data;

using static System.Convert;

namespace RustPlusApi.Fcm.Utils
{
    public static class DecryptionUtility
    {
        public static string Decrypt(DataMessageStanza dataMessage, Keys keys)
        {
            var cryptoKey = dataMessage.AppData.FirstOrDefault(item => item.Key == "crypto-key") ?? throw new Exception("crypto-key is missing");
            var salt = dataMessage.AppData.FirstOrDefault(item => item.Key == "encryption") ?? throw new Exception("salt is missing");

            using var dh = ECDiffieHellman.Create();
            dh.ImportECPrivateKey(FromBase64String(keys.PrivateKey), out _);

            var aesGcm = new AesGcm(FromBase64String(keys.AuthSecret)); //TODO: Check if this is correct

            var nonce = FromBase64String(cryptoKey.Value[3..]);
            var ciphertext = dataMessage.RawData.ToByteArray();
            var tag = FromBase64String(salt.Value[5..]);

            var decryptedBytes = new byte[ciphertext.Length];
            aesGcm.Decrypt(nonce, ciphertext, tag, decryptedBytes);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
    }
}
