using System.Security.Cryptography;
using System.Text;
using Stash.Runtime.Types;

namespace Stash.Tests.Interpreting;

public class CryptoTests : StashTestBase
{
    // ── MD5 ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Md5_EmptyString()
    {
        var result = Run(@"let result = crypto.md5("""");");
        Assert.Equal("d41d8cd98f00b204e9800998ecf8427e", result);
    }

    [Fact]
    public void Md5_Hello()
    {
        var result = Run(@"let result = crypto.md5(""hello"");");
        Assert.Equal("5d41402abc4b2a76b9719d911017c592", result);
    }

    [Fact]
    public void Md5_HelloWorld()
    {
        var result = Run(@"let result = crypto.md5(""Hello, World!"");");
        Assert.Equal("65a8e27d8879283831b664bd8b7f0ad4", result);
    }

    // ── SHA1 ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Sha1_EmptyString()
    {
        var result = Run(@"let result = crypto.sha1("""");");
        Assert.Equal("da39a3ee5e6b4b0d3255bfef95601890afd80709", result);
    }

    [Fact]
    public void Sha1_Hello()
    {
        var result = Run(@"let result = crypto.sha1(""hello"");");
        Assert.Equal("aaf4c61ddcc5e8a2dabede0f3b482cd9aea9434d", result);
    }

    // ── SHA256 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sha256_EmptyString()
    {
        var result = Run(@"let result = crypto.sha256("""");");
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", result);
    }

    [Fact]
    public void Sha256_Hello()
    {
        var result = Run(@"let result = crypto.sha256(""hello"");");
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", result);
    }

    [Fact]
    public void Sha256_HelloWorld()
    {
        var result = Run(@"let result = crypto.sha256(""Hello, World!"");");
        Assert.Equal("dffd6021bb2bd5b0af676290809ec3a53191dd81c7f70a4b28688a362182986f", result);
    }

    // ── SHA512 ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sha512_EmptyString()
    {
        var result = Run(@"let result = crypto.sha512("""");");
        Assert.Equal("cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e", result);
    }

    [Fact]
    public void Sha512_Hello()
    {
        var result = Run(@"let result = crypto.sha512(""hello"");");
        Assert.Equal("9b71d224bd62f3785d96d46ad3ea3d73319bfbc2890caadae2dff72519673ca72323c3d99ba5c11d7c7acc6e14b8c5da0c4663475c2e5c3adef46f73bcdec043", result);
    }

    // ── HMAC ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Hmac_Sha256_Basic()
    {
        var result = Run(@"let result = crypto.hmac(""sha256"", ""secret"", ""hello"");");
        Assert.Equal("88aab3ede8d3adf94d26ab90d3bafd4a2083070c3bcce9c014ee04a443847c0b", result);
    }

    [Fact]
    public void Hmac_Sha1_Basic()
    {
        var result = Run(@"let result = crypto.hmac(""sha1"", ""key"", ""message"");");
        Assert.Equal("2088df74d5f2146b48146caf4965377e9d0be3a4", result);
    }

    [Fact]
    public void Hmac_InvalidAlgo_Throws()
    {
        RunExpectingError(@"crypto.hmac(""sha999"", ""key"", ""data"");");
    }

    [Fact]
    public void Hmac_NonStringKey_Throws()
    {
        RunExpectingError(@"crypto.hmac(""sha256"", 42, ""data"");");
    }

    [Fact]
    public void Hmac_NonStringData_Throws()
    {
        RunExpectingError(@"crypto.hmac(""sha256"", ""key"", 42);");
    }

    // ── hashFile ─────────────────────────────────────────────────────────────

    [Fact]
    public void HashFile_Sha256()
    {
        const string content = "test content";
        var path = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        try
        {
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var result = Run($"let result = crypto.hashFile(\"{path}\");");
            Assert.Equal(expected, result);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void HashFile_Md5_ExplicitAlgo()
    {
        const string content = "hello";
        var path = System.IO.Path.GetTempFileName();
        System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
        try
        {
            var expected = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
            var result = Run($"let result = crypto.hashFile(\"{path}\", \"md5\");");
            Assert.Equal(expected, result);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [Fact]
    public void HashFile_NonexistentFile_Throws()
    {
        RunExpectingError(@"crypto.hashFile(""/tmp/does_not_exist_xyz_stash_test_abc.txt"");");
    }

    // ── UUID ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Uuid_ReturnsValidFormat()
    {
        var result = Run(@"let result = crypto.uuid();");
        var uuid = Assert.IsType<string>(result);
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", uuid);
    }

    [Fact]
    public void Uuid_Unique()
    {
        var a = (string)Run(@"let result = crypto.uuid();")!;
        var b = (string)Run(@"let result = crypto.uuid();")!;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Uuid_Length()
    {
        var result = Run(@"let result = crypto.uuid();");
        var uuid = Assert.IsType<string>(result);
        Assert.Equal(36, uuid.Length);
    }

    // ── randomBytes ──────────────────────────────────────────────────────────

    [Fact]
    public void RandomBytes_ReturnsCorrectLength()
    {
        // 16 bytes → byte[] — check via typeof and length
        var result = Run(@"let result = typeof(crypto.randomBytes(16));");
        Assert.Equal("byte[]", result);
    }

    [Fact]
    public void RandomBytes_Unique()
    {
        // With encoding param → string (backward compat)
        var a = (string)Run(@"let result = crypto.randomBytes(16, ""hex"");")!;
        var b = (string)Run(@"let result = crypto.randomBytes(16, ""hex"");")!;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void RandomBytes_ZeroThrows()
    {
        RunExpectingError(@"crypto.randomBytes(0);");
    }

    [Fact]
    public void RandomBytes_NegativeThrows()
    {
        RunExpectingError(@"crypto.randomBytes(-1);");
    }

    [Fact]
    public void RandomBytes_NonIntThrows()
    {
        RunExpectingError(@"crypto.randomBytes(""foo"");");
    }

    // ── Type validation ──────────────────────────────────────────────────────

    [Fact]
    public void Md5_NonStringThrows()
    {
        RunExpectingError(@"crypto.md5(42);");
    }

    [Fact]
    public void Sha256_NonStringThrows()
    {
        RunExpectingError(@"crypto.sha256(true);");
    }

    // ── Optional Args ────────────────────────────────────────────────────────

    [Fact]
    public void RandomBytes_WithBase64Encoding_ReturnsNonEmptyString()
    {
        var result = Run(@"let result = crypto.randomBytes(16, ""base64"");");
        var encoded = Assert.IsType<string>(result);
        Assert.NotEmpty(encoded);
    }

    [Fact]
    public void RandomBytes_WithHexEncoding_Returns32CharString()
    {
        var result = Run(@"let result = crypto.randomBytes(16, ""hex"");");
        var hex = Assert.IsType<string>(result);
        Assert.Equal(32, hex.Length);
    }

    // ── generateKey ───────────────────────────────────────────────────────────

    [Fact]
    public void GenerateKey_DefaultBits_Returns64CharHex()
    {
        var result = Run(@"let result = crypto.generateKey();");
        var hex = Assert.IsType<string>(result);
        Assert.Equal(64, hex.Length);
        Assert.Matches("^[0-9a-f]+$", hex);
    }

    [Fact]
    public void GenerateKey_128Bits_Returns32CharHex()
    {
        var result = Run(@"let result = crypto.generateKey(128);");
        var hex = Assert.IsType<string>(result);
        Assert.Equal(32, hex.Length);
    }

    [Fact]
    public void GenerateKey_192Bits_Returns48CharHex()
    {
        var result = Run(@"let result = crypto.generateKey(192);");
        var hex = Assert.IsType<string>(result);
        Assert.Equal(48, hex.Length);
    }

    [Fact]
    public void GenerateKey_256Bits_Returns64CharHex()
    {
        var result = Run(@"let result = crypto.generateKey(256);");
        var hex = Assert.IsType<string>(result);
        Assert.Equal(64, hex.Length);
    }

    [Fact]
    public void GenerateKey_InvalidBits_ThrowsError()
    {
        RunExpectingError(@"crypto.generateKey(512);");
    }

    [Fact]
    public void GenerateKey_ProducesUniqueKeys()
    {
        var a = (string)Run(@"let result = crypto.generateKey();")!;
        var b = (string)Run(@"let result = crypto.generateKey();")!;
        Assert.NotEqual(a, b);
    }

    // ── encrypt ───────────────────────────────────────────────────────────────

    [Fact]
    public void Encrypt_String_ReturnsDict()
    {
        var result = Run(@"let key = crypto.generateKey(); let result = crypto.encrypt(""hello"", key);");
        Assert.IsType<StashDictionary>(result);
    }

    [Fact]
    public void Encrypt_ReturnsNonNullIv()
    {
        var result = Run(@"let key = crypto.generateKey(); let enc = crypto.encrypt(""hello"", key); let result = enc.iv;");
        var iv = Assert.IsType<string>(result);
        Assert.Equal(24, iv.Length); // 12 bytes = 24 hex chars
        Assert.Matches("^[0-9a-f]+$", iv);
    }

    [Fact]
    public void Encrypt_ReturnsNonNullTag()
    {
        var result = Run(@"let key = crypto.generateKey(); let enc = crypto.encrypt(""hello"", key); let result = enc.tag;");
        var tag = Assert.IsType<string>(result);
        Assert.Equal(32, tag.Length); // 16 bytes = 32 hex chars
        Assert.Matches("^[0-9a-f]+$", tag);
    }

    [Fact]
    public void Encrypt_DifferentCallsProduceDifferentCiphertexts()
    {
        // Same key, different IVs per call → different ciphertexts
        const string fixedKey = "0000000000000000000000000000000000000000000000000000000000000000";
        var a = (string)Run($@"let result = crypto.encrypt(""hello"", ""{fixedKey}"").ciphertext;")!;
        var b = (string)Run($@"let result = crypto.encrypt(""hello"", ""{fixedKey}"").ciphertext;")!;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Encrypt_EmptyString_Success()
    {
        var result = Run(@"let key = crypto.generateKey(); let result = crypto.encrypt("""", key);");
        var d = Assert.IsType<StashDictionary>(result);
        Assert.NotNull(d.Get("ciphertext").ToObject());
        Assert.NotNull(d.Get("iv").ToObject());
        Assert.NotNull(d.Get("tag").ToObject());
    }

    [Fact]
    public void Encrypt_LongString_Success()
    {
        var result = Run(@"let key = crypto.generateKey(); let big = ""abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz01234""; let result = crypto.encrypt(big, key);");
        Assert.IsType<StashDictionary>(result);
    }

    [Fact]
    public void Encrypt_KeyTooShort_ThrowsError()
    {
        // 16 hex chars = 8 bytes — too short for AES-256
        RunExpectingError(@"crypto.encrypt(""hello"", ""0000000000000000"");");
    }

    [Fact]
    public void Encrypt_KeyTooLong_ThrowsError()
    {
        // 66 hex chars = 33 bytes — too long for AES-256
        RunExpectingError(@"crypto.encrypt(""hello"", ""000000000000000000000000000000000000000000000000000000000000000000"");");
    }

    // ── decrypt ───────────────────────────────────────────────────────────────

    [Fact]
    public void Decrypt_EncryptedString_ReturnsOriginal()
    {
        var result = Run(@"
            let key = crypto.generateKey();
            let enc = crypto.encrypt(""hello world"", key);
            let result = crypto.decrypt(enc, key);
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Decrypt_WithDictCiphertext_ReturnsOriginal()
    {
        var result = Run(@"
            let key = crypto.generateKey();
            let enc = crypto.encrypt(""hello world"", key);
            let d = { ciphertext: enc.ciphertext, iv: enc.iv, tag: enc.tag };
            let result = crypto.decrypt(d, key);
        ");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Decrypt_WrongKey_ThrowsError()
    {
        RunExpectingError(@"
            let key1 = crypto.generateKey();
            let key2 = crypto.generateKey();
            let enc = crypto.encrypt(""hello"", key1);
            crypto.decrypt(enc, key2);
        ");
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsError()
    {
        RunExpectingError(@"
            let key = crypto.generateKey();
            let enc = crypto.encrypt(""hello"", key);
            let tampered = { ""ciphertext"": enc.ciphertext, ""iv"": enc.iv, ""tag"": ""00000000000000000000000000000000"" };
            crypto.decrypt(tampered, key);
        ");
    }

    // ── roundtrip ────────────────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_EncryptDecrypt_Identical()
    {
        var result = Run(@"
            let key = crypto.generateKey();
            let plaintext = ""The quick brown fox jumps over the lazy dog"";
            let enc = crypto.encrypt(plaintext, key);
            let result = crypto.decrypt(enc, key);
        ");
        Assert.Equal("The quick brown fox jumps over the lazy dog", result);
    }

    [Fact]
    public void Roundtrip_EmptyString_Identical()
    {
        var result = Run(@"
            let key = crypto.generateKey();
            let enc = crypto.encrypt("""", key);
            let result = crypto.decrypt(enc, key);
        ");
        Assert.Equal("", result);
    }

    [Fact]
    public void Roundtrip_UnicodeString_Identical()
    {
        var result = Run(@"
            let key = crypto.generateKey();
            let enc = crypto.encrypt(""héllo wörld ☺"", key);
            let result = crypto.decrypt(enc, key);
        ");
        Assert.Equal("héllo wörld ☺", result);
    }
}
