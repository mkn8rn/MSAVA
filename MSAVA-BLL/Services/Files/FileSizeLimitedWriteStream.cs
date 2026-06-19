using MSAVA_Shared.Models;

namespace MSAVA_BLL.Services.Files;

internal sealed class FileSizeLimitedWriteStream : Stream
{
    private readonly Stream _destination;
    private readonly long _maximumFileSizeBytes;
    private long _bytesWritten;

    public FileSizeLimitedWriteStream(Stream destination, long maximumFileSizeBytes)
    {
        _destination = destination ?? throw new ArgumentNullException(nameof(destination));
        _maximumFileSizeBytes = FileSizePolicy.RequireValidMaximum(maximumFileSizeBytes);
    }

    public long BytesWritten => _bytesWritten;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => _destination.CanWrite;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _bytesWritten;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
        _destination.Flush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        return _destination.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateWrite(count);
        _destination.Write(buffer, offset, count);
        _bytesWritten += count;
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        ValidateWrite(buffer.Length);
        _destination.Write(buffer);
        _bytesWritten += buffer.Length;
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        ValidateWrite(count);
        return WriteArrayAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        ValidateWrite(buffer.Length);
        return WriteMemoryAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        ValidateWrite(1);
        _destination.WriteByte(value);
        _bytesWritten++;
    }

    private async Task WriteArrayAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        await _destination.WriteAsync(buffer, offset, count, cancellationToken);
        _bytesWritten += count;
    }

    private async ValueTask WriteMemoryAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken)
    {
        await _destination.WriteAsync(buffer, cancellationToken);
        _bytesWritten += buffer.Length;
    }

    private void ValidateWrite(int byteCount)
    {
        if (!CanWrite)
            throw new NotSupportedException("The destination stream is not writable.");

        FileSizePolicy.EnsureChunkWithinMaximum(_bytesWritten, byteCount, _maximumFileSizeBytes);
    }
}
