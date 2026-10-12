namespace FileManagerClient.Api;

/// <summary>
/// Обёртка над потоком для чтения: сообщает, сколько байт прочитано (нарастающим итогом).
/// Используется для содержимого загрузки — HttpClient вычитывает поток по мере отправки.
/// </summary>
internal sealed class ProgressStream(Stream inner, IProgress<long> progress) : Stream
{
    private long _total;

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => inner.Position = value;
    }

    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => Track(await inner.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => Track(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    private int Track(int read)
    {
        if (read > 0)
            progress.Report(_total += read);
        return read;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
