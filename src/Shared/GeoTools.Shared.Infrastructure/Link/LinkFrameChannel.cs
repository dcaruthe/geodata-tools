using System.Buffers.Binary;
using System.Security.Cryptography;
using GeoTools.Shared.Application.Link;

namespace GeoTools.Shared.Infrastructure.Link;

internal sealed class LinkKeyRing(byte[] key)
{
    public byte[] Key { get; set; } = key;
}

// Each frame is length(4) | version(1) | kind(1) | sequence(8) | nonce(12) | ciphertext | tag(16).
// The first ten bytes are the additional authenticated data, so a reordered or forged header
// fails authentication instead of being trusted.
internal sealed class LinkFrameWriter(Stream stream, LinkKeyRing ring)
{
    private long _sequence;

    public long Sequence => _sequence;

    public async Task WriteAsync(
        LinkFrameKind kind,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken
    )
    {
        var sequence = _sequence + 1;
        var frame = new byte[
            4 + 1 + 1 + 8 + LinkProtocol.NonceBytes + payload.Length + LinkProtocol.TagBytes
        ];
        BinaryPrimitives.WriteInt32BigEndian(frame, frame.Length - 4);
        frame[4] = (byte)LinkProtocol.Version;
        frame[5] = (byte)kind;
        BinaryPrimitives.WriteInt64BigEndian(frame.AsSpan(6), sequence);
        var nonce = frame.AsSpan(14, LinkProtocol.NonceBytes);
        RandomNumberGenerator.Fill(nonce);

        var tag = frame.AsSpan(frame.Length - LinkProtocol.TagBytes);
        using var aes = new AesGcm(ring.Key, LinkProtocol.TagBytes);
        aes.Encrypt(
            nonce,
            payload.Span,
            frame.AsSpan(26, payload.Length),
            tag,
            frame.AsSpan(4, 10)
        );

        await stream.WriteAsync(frame, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        _sequence = sequence;
    }
}

internal sealed class LinkFrameReader(Stream stream, LinkKeyRing ring)
{
    private long _sequence;

    public long Sequence => _sequence;

    public async Task<(LinkFrameKind Kind, byte[] Payload)> ReadAsync(
        CancellationToken cancellationToken
    )
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        var minimum = 1 + 1 + 8 + LinkProtocol.NonceBytes + LinkProtocol.TagBytes;
        if (length < minimum || length > LinkProtocol.MaxFrameBytes)
            throw new InvalidDataException($"Link frame length {length} is out of range.");

        var frame = new byte[length];
        await stream.ReadExactlyAsync(frame, cancellationToken);

        if (frame[0] != LinkProtocol.Version)
            throw new InvalidDataException($"Link protocol version {frame[0]} is not supported.");

        var kind = (LinkFrameKind)frame[1];
        var sequence = BinaryPrimitives.ReadInt64BigEndian(frame.AsSpan(2));
        if (sequence != _sequence + 1)
            throw new InvalidDataException(
                $"Link frame sequence {sequence} is not {_sequence + 1}."
            );

        var nonce = frame.AsSpan(10, LinkProtocol.NonceBytes);
        var cipherLength = length - minimum;
        var cipher = frame.AsSpan(22, cipherLength);
        var tag = frame.AsSpan(22 + cipherLength, LinkProtocol.TagBytes);
        var payload = new byte[cipherLength];

        using var aes = new AesGcm(ring.Key, LinkProtocol.TagBytes);
        aes.Decrypt(nonce, cipher, tag, payload, frame.AsSpan(0, 10));

        _sequence = sequence;
        return (kind, payload);
    }
}
