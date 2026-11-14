// File:    StreamCiphers.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;

namespace MasterSafe.Backup;

/// <summary>
/// ChaCha20 (RFC 8439) and Salsa20 keystreams, which KeePass files use and .NET does not expose on their own. Only used to
/// read KeePass files: they are checked against the RFC test vectors, and nothing in MasterSafe's own formats relies on them.
/// </summary>
public sealed class StreamCipher : IDisposable
{
    private readonly uint[] _state = new uint[16];
    private readonly uint[] _work = new uint[16];
    private readonly byte[] _block = new byte[64];
    private readonly bool _salsa;
    private int _used = 64;

    private StreamCipher(bool salsa) => _salsa = salsa;

    /// <summary>ChaCha20 with a 256-bit key, a 96-bit nonce and a block counter.</summary>
    public static StreamCipher ChaCha20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, uint counter = 0)
    {
        if (key.Length != 32 || nonce.Length != 12)
        {
            throw new ArgumentException("ChaCha20 needs a 32-byte key and a 12-byte nonce.");
        }

        var cipher = new StreamCipher(salsa: false);
        var s = cipher._state;
        s[0] = 0x61707865; s[1] = 0x3320646e; s[2] = 0x79622d32; s[3] = 0x6b206574;
        for (var i = 0; i < 8; i++)
        {
            s[4 + i] = BinaryPrimitives.ReadUInt32LittleEndian(key[(i * 4)..]);
        }

        s[12] = counter;
        for (var i = 0; i < 3; i++)
        {
            s[13 + i] = BinaryPrimitives.ReadUInt32LittleEndian(nonce[(i * 4)..]);
        }

        return cipher;
    }

    /// <summary>Salsa20/20 with a 256-bit key and a 64-bit nonce.</summary>
    public static StreamCipher Salsa20(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce)
    {
        if (key.Length != 32 || nonce.Length != 8)
        {
            throw new ArgumentException("Salsa20 needs a 32-byte key and an 8-byte nonce.");
        }

        var cipher = new StreamCipher(salsa: true);
        var s = cipher._state;
        uint Word(ReadOnlySpan<byte> b, int i) => BinaryPrimitives.ReadUInt32LittleEndian(b[(i * 4)..]);
        s[0] = 0x61707865; s[5] = 0x3320646e; s[10] = 0x79622d32; s[15] = 0x6b206574;
        s[1] = Word(key, 0); s[2] = Word(key, 1); s[3] = Word(key, 2); s[4] = Word(key, 3);
        s[11] = Word(key, 4); s[12] = Word(key, 5); s[13] = Word(key, 6); s[14] = Word(key, 7);
        s[6] = Word(nonce, 0); s[7] = Word(nonce, 1);
        return cipher;
    }

    /// <summary>XORs the keystream into the data, continuing where the last call stopped.</summary>
    public void Apply(Span<byte> data)
    {
        for (var i = 0; i < data.Length; i++)
        {
            if (_used == 64)
            {
                NextBlock();
            }

            data[i] ^= _block[_used++];
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_state.AsSpan()));
        CryptographicOperations.ZeroMemory(System.Runtime.InteropServices.MemoryMarshal.AsBytes(_work.AsSpan()));
        CryptographicOperations.ZeroMemory(_block);
    }

    private void NextBlock()
    {
        var x = _work;
        _state.CopyTo(x, 0);
        for (var round = 0; round < 10; round++)
        {
            if (_salsa)
            {
                SalsaQuarter(x, 0, 4, 8, 12); SalsaQuarter(x, 5, 9, 13, 1); SalsaQuarter(x, 10, 14, 2, 6); SalsaQuarter(x, 15, 3, 7, 11);
                SalsaQuarter(x, 0, 1, 2, 3); SalsaQuarter(x, 5, 6, 7, 4); SalsaQuarter(x, 10, 11, 8, 9); SalsaQuarter(x, 15, 12, 13, 14);
            }
            else
            {
                ChaChaQuarter(x, 0, 4, 8, 12); ChaChaQuarter(x, 1, 5, 9, 13); ChaChaQuarter(x, 2, 6, 10, 14); ChaChaQuarter(x, 3, 7, 11, 15);
                ChaChaQuarter(x, 0, 5, 10, 15); ChaChaQuarter(x, 1, 6, 11, 12); ChaChaQuarter(x, 2, 7, 8, 13); ChaChaQuarter(x, 3, 4, 9, 14);
            }
        }

        for (var i = 0; i < 16; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(_block.AsSpan(i * 4), x[i] + _state[i]);
        }

        // The block counter: word 12 for ChaCha20, words 8 and 9 for Salsa20.
        if (_salsa)
        {
            if (++_state[8] == 0)
            {
                _state[9]++;
            }
        }
        else if (++_state[12] == 0)
        {
            throw new CryptographicException("The ChaCha20 block counter ran out.");
        }

        _used = 0;
    }

    private static void ChaChaQuarter(uint[] x, int a, int b, int c, int d)
    {
        x[a] += x[b]; x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 16);
        x[c] += x[d]; x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 12);
        x[a] += x[b]; x[d] = BitOperations.RotateLeft(x[d] ^ x[a], 8);
        x[c] += x[d]; x[b] = BitOperations.RotateLeft(x[b] ^ x[c], 7);
    }

    private static void SalsaQuarter(uint[] x, int a, int b, int c, int d)
    {
        x[b] ^= BitOperations.RotateLeft(x[a] + x[d], 7);
        x[c] ^= BitOperations.RotateLeft(x[b] + x[a], 9);
        x[d] ^= BitOperations.RotateLeft(x[c] + x[b], 13);
        x[a] ^= BitOperations.RotateLeft(x[d] + x[c], 18);
    }
}
