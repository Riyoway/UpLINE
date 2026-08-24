using System.Numerics;
using System.Security.Cryptography;
using UpLINE.Line.Models;

namespace UpLINE.Line.Auth;

public static class X25519
{
    private static readonly BigInteger Prime = (BigInteger.One << 255) - 19;
    private static readonly BigInteger A24 = 121665;

    public static X25519KeyPair GenerateKeyPair()
    {
        var privateKey = RandomNumberGenerator.GetBytes(32);
        privateKey[0] &= 248;
        privateKey[31] &= 127;
        privateKey[31] |= 64;

        var basePoint = new byte[32];
        basePoint[0] = 9;
        var publicKey = ScalarMultiply(privateKey, basePoint);
        return new X25519KeyPair(privateKey, publicKey);
    }

    public static byte[] ScalarMultiply(ReadOnlySpan<byte> scalar, ReadOnlySpan<byte> uCoordinate)
    {
        if (scalar.Length != 32 || uCoordinate.Length != 32)
            throw new ArgumentException("X25519 keys must be 32 bytes.");

        var k = scalar.ToArray();
        k[0] &= 248;
        k[31] &= 127;
        k[31] |= 64;
        var x1 = FromLittleEndian(uCoordinate) % Prime;
        var x2 = BigInteger.One;
        var z2 = BigInteger.Zero;
        var x3 = x1;
        var z3 = BigInteger.One;
        var swap = 0;

        for (var t = 254; t >= 0; t--)
        {
            var kt = (k[t >> 3] >> (t & 7)) & 1;
            swap ^= kt;
            if (swap != 0)
            {
                (x2, x3) = (x3, x2);
                (z2, z3) = (z3, z2);
            }
            swap = kt;

            var a = Mod(x2 + z2);
            var aa = Mod(a * a);
            var b = Mod(x2 - z2);
            var bb = Mod(b * b);
            var e = Mod(aa - bb);
            var c = Mod(x3 + z3);
            var d = Mod(x3 - z3);
            var da = Mod(d * a);
            var cb = Mod(c * b);
            x3 = Mod((da + cb) * (da + cb));
            z3 = Mod(x1 * (da - cb) * (da - cb));
            x2 = Mod(aa * bb);
            z2 = Mod(e * (aa + A24 * e));
        }

        if (swap != 0)
        {
            (x2, x3) = (x3, x2);
            (z2, z3) = (z3, z2);
        }

        var result = Mod(x2 * BigInteger.ModPow(z2, Prime - 2, Prime));
        return ToLittleEndian(result);
    }

    private static BigInteger FromLittleEndian(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        copy[31] &= 127;
        return new BigInteger(copy, isUnsigned: true, isBigEndian: false);
    }

    private static byte[] ToLittleEndian(BigInteger value)
    {
        var result = new byte[32];
        var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
        Array.Copy(bytes, result, Math.Min(bytes.Length, result.Length));
        return result;
    }

    private static BigInteger Mod(BigInteger value)
    {
        var result = value % Prime;
        return result.Sign < 0 ? result + Prime : result;
    }
}
