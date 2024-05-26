using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using McsProto;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Asn1.X9;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using RustPlusApi.Fcm.Data;
using ECCurve = Org.BouncyCastle.Math.EC.ECCurve;

namespace RustPlusApi.Fcm.Utils
{
	internal class DecryptionUtility
	{
		private const int NonceBitSize = 128;
		private const int MacBitSize = 128;
		private const int SHA_256_LENGTH = 32;
		private const int KEY_LENGTH = 16;
		private const int NONCE_LENGTH = 12;
		private const int HEADER_RS = 4096;
		private const int TAG_LENGTH = 16;
		private const int CHUNK_SIZE = HEADER_RS + TAG_LENGTH;
		private const int PADSIZE = 2;
		private static readonly ECDomainParameters ecDomainParameters;
		private static readonly ECKeyGenerationParameters eckgparameters;
		private static readonly ECCurve ecCurve;
		private static readonly ECDomainParameters ecSpec;
		private static readonly X9ECParameters ecParams = NistNamedCurves.GetByName("P-256");
		private static readonly SecureRandom secureRandom = new();

		// CEK_INFO = "Content-Encoding: aesgcm" || 0x00
		private static readonly byte[] keyInfoParameter = Encoding.ASCII.GetBytes("Content-Encoding: aesgcm\0");

		// NONCE_INFO = "Content-Encoding: nonce" || 0x00
		private static readonly byte[] _nonceInfoParameter = Encoding.ASCII.GetBytes("Content-Encoding: nonce\0");
		private static readonly byte[] _authInfoParameter = Encoding.ASCII.GetBytes("Content-Encoding: auth\0");
		private static readonly byte[] _keyLabel = Encoding.ASCII.GetBytes("P-256");

		private readonly ECPrivateKeyParameters _privateKey;
		private readonly ECPublicKeyParameters _publicKey;

		static DecryptionUtility()
		{
			ecCurve = ecParams.Curve;
			ecSpec = new ECDomainParameters(ecCurve, ecParams.G, ecParams.N, ecParams.H, ecParams.GetSeed());

			eckgparameters = new ECKeyGenerationParameters(ecSpec, secureRandom);
			ecDomainParameters = eckgparameters.DomainParameters;
		}

		public DecryptionUtility()
		{
			(_privateKey, _publicKey) = GenerateKeys();
			PublicKey = _publicKey.Q.GetEncoded();

			AuthSecret = new byte[16];
			secureRandom.NextBytes(AuthSecret);
		}

		internal DecryptionUtility(byte[] publicKey, byte[] privateKey, byte[] authSecret)
		{
			var pt = ecCurve.DecodePoint(publicKey);
            _publicKey = new ECPublicKeyParameters(pt, ecDomainParameters);
            _privateKey = new ECPrivateKeyParameters(new BigInteger(1, privateKey), ecDomainParameters);

			AuthSecret = authSecret;
			PublicKey = _publicKey.Q.GetEncoded();
		}

		public byte[] AuthSecret { get; }
		public byte[] PublicKey { get; }

		private byte[] AddLengthPrefix(byte[] buffer)
		{
			var newBuffer = new byte[buffer.Length + 2];
			Array.Copy(buffer, 0, newBuffer, 2, buffer.Length);

			var intBytes = BitConverter.GetBytes((short)buffer.Length);

			if (BitConverter.IsLittleEndian) Array.Reverse(intBytes);

			Debug.Assert(intBytes.Length <= 2);
			Array.Copy(intBytes, 0, newBuffer, 0, intBytes.Length);

			return newBuffer;
		}

		public byte[] Decrypt(byte[] buffer, byte[] senderPublicKeyBytes, byte[] salt)
		{
			var ecP = NistNamedCurves.GetByName("P-256");
			var eCDomainParameters = new ECDomainParameters(ecP.Curve, ecP.G, ecP.N);

			var pt = ecP.Curve.DecodePoint(senderPublicKeyBytes);
			var senderPublicKey = new ECPublicKeyParameters(pt, eCDomainParameters);

			var (key, nonce) = DeriveKeyAndNonce(salt, AuthSecret, senderPublicKey, _publicKey, _privateKey);

			var result = new byte[0];
			var start = 0;

			// TODO: this is not tested with more than one iteration
			for (uint i = 0; start < buffer.Length; ++i)
			{
				var end = start + CHUNK_SIZE;
				if (end == buffer.Length) throw new InvalidOperationException("Truncated payload");

				end = Math.Min(end, buffer.Length);

				if (end - start <= TAG_LENGTH) throw new InvalidOperationException("Invalid block: too small at " + i);

				var block = DecryptRecord(key, nonce, i, ByteArray.Slice(buffer, start, end), end >= buffer.Length);
				result = ByteArray.Concat(result, block);
				start = end;
			}

			return result;
		}

		private byte[] RemovePad(byte[] buffer)
		{
			var pad = (int)ByteArray.ReadUInt64(buffer, 0, PADSIZE);

			if (pad + PADSIZE > buffer.Length) throw new InvalidOperationException("padding exceeds block size");

			return ByteArray.Slice(buffer, pad + PADSIZE, buffer.Length);
		}

		private byte[] DecryptRecord(byte[] key, byte[] nonce, uint counter, byte[] buffer, bool last)
		{
			nonce = GenerateNonce(nonce, counter);

			var blockCipher = new GcmBlockCipher(new AesEngine());

			blockCipher.Init(false, new AeadParameters(new KeyParameter(key), 128, nonce));

			var decryptedMessage = new byte[blockCipher.GetOutputSize(buffer.Length)];

			var decryptedMessageLength = blockCipher.ProcessBytes(buffer, 0, buffer.Length, decryptedMessage, 0);

			decryptedMessageLength += blockCipher.DoFinal(decryptedMessage, decryptedMessageLength);

			return RemovePad(decryptedMessage);
		}

		private (byte[], byte[]) DeriveKeyAndNonce(byte[] salt, byte[] authSecret, ECPublicKeyParameters senderPublicKey,
			ECPublicKeyParameters receiverPublicKey, ECPrivateKeyParameters receiverPrivateKey)
		{
			var (secret, context) = ExtractSecretAndContext(senderPublicKey, receiverPublicKey, receiverPrivateKey);
			secret = HKDF.GetBytes(authSecret, secret, _authInfoParameter, SHA_256_LENGTH);

			var keyInfo = ByteArray.Concat(keyInfoParameter, context);
			var nonceInfo = ByteArray.Concat(_nonceInfoParameter, context);

			var prk = HKDF.Extract(salt, secret);

			return (HKDF.Expand(prk, keyInfo, KEY_LENGTH), HKDF.Expand(prk, nonceInfo, NONCE_LENGTH));
		}

		private (byte[], byte[]) ExtractSecretAndContext(ECPublicKeyParameters senderPublicKey,
			ECPublicKeyParameters receiverPublicKey, ECPrivateKeyParameters receiverPrivateKey)
		{
			IBasicAgreement aKeyAgree = new ECDHBasicAgreement();

			aKeyAgree.Init(receiverPrivateKey);
			var sharedSecret = aKeyAgree.CalculateAgreement(senderPublicKey).ToByteArrayUnsigned();

			var receiverKeyBytes = AddLengthPrefix(receiverPublicKey.Q.GetEncoded());
			var senderPublicKeyBytes = AddLengthPrefix(senderPublicKey.Q.GetEncoded());

			var context = new byte[_keyLabel.Length + 1 + receiverKeyBytes.Length + senderPublicKeyBytes.Length];

			var destinationOffset = 0;
			Array.Copy(_keyLabel, 0, context, destinationOffset, _keyLabel.Length);
			destinationOffset += _keyLabel.Length + 1;
			Array.Copy(receiverKeyBytes, 0, context, destinationOffset, receiverKeyBytes.Length);
			destinationOffset += receiverKeyBytes.Length;
			Array.Copy(senderPublicKeyBytes, 0, context, destinationOffset, senderPublicKeyBytes.Length);

			return (sharedSecret, context);
		}

		private byte[] GenerateNonce(byte[] buffer, uint counter)
		{
			var nonce = new byte[buffer.Length];
			Buffer.BlockCopy(buffer, 0, nonce, 0, buffer.Length);
			var m = ByteArray.ReadUInt64(nonce, nonce.Length - 6, 6);
			var x = ((m ^ counter) & 0xffffff) + (((m / 0x1000000) ^ (counter / 0x1000000)) & 0xffffff) * 0x1000000;
			ByteArray.WriteUInt64(nonce, m, nonce.Length - 6, 6);

			return nonce;
		}

		private (ECPrivateKeyParameters, ECPublicKeyParameters) GenerateKeys()
		{
			var gen = new ECKeyPairGenerator("ECDH");
			gen.Init(eckgparameters);
			var eckp = gen.GenerateKeyPair();

			var ecPub = (ECPublicKeyParameters)eckp.Public;
			var ecPri = (ECPrivateKeyParameters)eckp.Private;

			return (ecPri, ecPub);
		}

		private static byte[] DecodeBase64(string base64)
		{
			base64 = base64.Replace('-', '+').Replace('_', '/');

			while (base64.Length % 4 != 0) base64 += "=";

			return Convert.FromBase64String(base64);
		}

		public static string Decrypt(DataMessageStanza dataMessage, Keys keys)
		{
			var decryptor = new DecryptionUtility(DecodeBase64(keys.PublicKey), DecodeBase64(keys.PrivateKey),
				DecodeBase64(keys.AuthSecret));
			var cryptoKey = dataMessage.AppDatas.FirstOrDefault(item => item.Key == "crypto-key") ??
							throw new Exception("crypto-key is missing");
			var salt = dataMessage.AppDatas.FirstOrDefault(item => item.Key == "encryption") ??
					   throw new Exception("salt is missing");
			var decryptedBytes = decryptor.Decrypt(dataMessage.RawData, DecodeBase64(cryptoKey.Value[3..]),
				DecodeBase64(salt.Value[5..]));

			return Encoding.UTF8.GetString(decryptedBytes);
		}

		private static class ByteArray
		{
			// TODO: optimize this method to use pointers or bit shifts
			// check if it is working with big endian
			public static ulong ReadUInt64(byte[] bytes, int startIndex, int length)
			{
				var buffer = new byte[8];
				Buffer.BlockCopy(bytes, startIndex, buffer, 8 - length, length);

				if (BitConverter.IsLittleEndian) Array.Reverse(buffer);

				return BitConverter.ToUInt64(buffer, 0);
			}

			// TODO: check if it is working with big endian
			public static void WriteUInt64(byte[] source, ulong value, int startIndex, int length)
			{
				var buffer = BitConverter.GetBytes(value);
				Buffer.BlockCopy(source, startIndex, buffer, 0, length);
			}

			public static byte[] Slice(byte[] source, int startIndex, int endIndex)
			{
				Debug.Assert(startIndex < endIndex);

				var length = endIndex - startIndex;
				var result = new byte[length];
				Buffer.BlockCopy(source, startIndex, result, 0, length);
				return result;
			}

			public static byte[] Concat(byte[] first, byte[] second)
			{
				var ret = new byte[first.Length + second.Length];
				Buffer.BlockCopy(first, 0, ret, 0, first.Length);
				Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
				return ret;
			}
		}

		private class HKDF
		{
			/// <summary>
			///     Returns a 32 byte psuedorandom number that can be used with the Expand method if
			///     a cryptographically secure pseudorandom number is not already available.
			/// </summary>
			/// <param name="salt">
			///     (Optional, but you should use it) Non-secret random value.
			///     If less than 64 bytes it is padded with zeros. Can be reused but output is
			///     stronger if not reused. (And of course output is much stronger with salt than
			///     without it)
			/// </param>
			/// <param name="inputKeyMaterial">
			///     Material that is not necessarily random that
			///     will be used with the HMACSHA256 hash function and the salt to produce
			///     a 32 byte psuedorandom number.
			/// </param>
			/// <returns></returns>
			public static byte[] Extract(byte[] salt, byte[] inputKeyMaterial)
			{
				//For algorithm docs, see section 2.2: https://tools.ietf.org/html/rfc5869 

				using (var hmac = new HMACSHA256(salt))
				{
					return hmac.ComputeHash(inputKeyMaterial, 0, inputKeyMaterial.Length);
				}
			}


			/// <summary>
			///     Returns a secure pseudorandom key of the desired length. Useful as a key derivation
			///     function to derive one cryptograpically secure pseudorandom key from another
			///     cryptograpically secure pseudorandom key. This can be useful, for example,
			///     when needing to create a subKey from a master key.
			/// </summary>
			/// <param name="key">
			///     A cryptograpically secure pseudorandom number. Can be obtained
			///     via the Extract method or elsewhere. Must be 32 bytes or greater. 64 bytes is
			///     the prefered size.  Shorter keys are padded to 64 bytes, longer ones are hashed
			///     to 64 bytes.
			/// </param>
			/// <param name="info">
			///     (Optional) Context and application specific information.
			///     Allows the output to be bound to application context related information.
			/// </param>
			/// <param name="length">Length of output in bytes.</param>
			/// <returns></returns>
			public static byte[] Expand(byte[] key, byte[] info, int length)
			{
				//For algorithm docs, see section 2.3: https://tools.ietf.org/html/rfc5869 
				//Also note:
				//       SHA256 has a block size of 64 bytes
				//       SHA256 has an output length of 32 bytes (but can be truncated to less)

				const int hashLength = 32;

				//Min recommended length for Key is the size of the hash output (32 bytes in this case)
				//See section 2: https://tools.ietf.org/html/rfc2104#section-3
				//Also see:      http://security.stackexchange.com/questions/95972/what-are-requirements-for-hmac-secret-key
				if (key == null || key.Length < 32)
					throw new ArgumentOutOfRangeException("Key should be 32 bytes or greater.");

				if (length > 255 * hashLength)
					throw new ArgumentOutOfRangeException(
						"Output length must 8160 bytes or less which is 255 * the SHA256 block site of 32 bytes.");

				var outputIndex = 0;
				byte[] buffer;
				var hash = new byte[0];
				var output = new byte[length];
				var count = 1;
				int bytesToCopy;

				using (var hmac = new HMACSHA256(key))
				{
					while (outputIndex < length)
					{
						//Setup buffer to hash
						buffer = new byte[hash.Length + info.Length + 1];
						Buffer.BlockCopy(hash, 0, buffer, 0, hash.Length);
						Buffer.BlockCopy(info, 0, buffer, hash.Length, info.Length);
						buffer[buffer.Length - 1] = (byte)count++;

						//Hash the buffer and return a 32 byte hash
						hash = hmac.ComputeHash(buffer, 0, buffer.Length);

						//Copy as much of the hash as we need to the final output
						bytesToCopy = Math.Min(length - outputIndex, hash.Length);
						Buffer.BlockCopy(hash, 0, output, outputIndex, bytesToCopy);
						outputIndex += bytesToCopy;
					}
				}

				return output;
			}


			/// <summary>
			///     Generates a psuedorandom number of the length specified.  This number is suitable
			///     for use as an encryption key, HMAC validation key or other uses of a cryptographically
			///     secure psuedorandom number.
			/// </summary>
			/// <param name="salt">
			///     non-secret random value. If less than 64 bytes it is
			///     padded with zeros. Can be reused but output is stronger if not reused.
			/// </param>
			/// <param name="inputKeyMaterial">
			///     Material that is not necessarily random that
			///     will be used with the HMACSHA256 hash function and the salt to produce
			///     a 32 byte psuedorandom number.
			/// </param>
			/// <param name="info">
			///     (Optional) context and application specific information.
			///     Allows the output to be bound to application context related information. Pass 0 length
			///     byte array to omit.
			/// </param>
			/// <param name="length">Length of output in bytes.</param>
			public static byte[] GetBytes(byte[] salt, byte[] inputKeyMaterial, byte[] info, int length)
			{
				var key = Extract(salt, inputKeyMaterial);
				return Expand(key, info, length);
			}
		}
	}
}