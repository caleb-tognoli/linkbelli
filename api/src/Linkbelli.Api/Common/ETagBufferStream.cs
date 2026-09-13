namespace Linkbelli.Api.Common;

/// <summary>
/// Holds a response body in memory only while it is still small enough to be worth hashing.
/// </summary>
/// <remarks>
/// The filter that uses this needs the finished bytes before it can tag them, which means
/// buffering. What it does not need is to buffer <em>everything</em>: an export, a backup or a
/// full-text search page can be megabytes, and those were being copied into a managed array — and
/// then copied again by <c>ToArray</c> — to compute a header that is discarded a line later.
///
/// So this buffers up to a cap and gives up cleanly past it: what has accumulated is flushed to
/// the real stream and every later write goes straight through, which is what would have happened
/// with no filter at all. The decision is also taken once, on the first write, for a response
/// whose content type says it was never a candidate.
/// </remarks>
public sealed class ETagBufferStream(Stream inner, int maxBufferedBytes, Func<bool> shouldBuffer) : Stream
{
    private MemoryStream? _buffer = new();
    private bool _decided;

    /// <summary>True once the body stopped being buffered and went to the real stream.</summary>
    public bool PassedThrough { get; private set; }

    /// <summary>The buffered body, valid only while <see cref="PassedThrough"/> is false.</summary>
    public ReadOnlyMemory<byte> Buffered =>
        _buffer is null ? ReadOnlyMemory<byte>.Empty : _buffer.GetBuffer().AsMemory(0, (int)_buffer.Length);

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count) =>
        Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (Accepts(buffer.Length))
        {
            _buffer!.Write(buffer);
            return;
        }

        inner.Write(buffer);
    }

    public override async ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (Accepts(buffer.Length))
        {
            _buffer!.Write(buffer.Span);
            return;
        }

        // Whatever had already accumulated goes first, or the body arrives out of order.
        await DrainAsync(cancellationToken);
        await inner.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <summary>
    /// Whether this write still belongs in the buffer, switching to pass-through if it does not.
    /// </summary>
    private bool Accepts(int incoming)
    {
        if (PassedThrough)
        {
            return false;
        }

        if (!_decided)
        {
            _decided = true;

            // Asked once, on the first write: by now the handler has set the content type, and a
            // response that was never going to be tagged should not be copied at all.
            if (!shouldBuffer())
            {
                PassedThrough = true;
                _buffer = null;
                return false;
            }
        }

        if (_buffer!.Length + incoming <= maxBufferedBytes)
        {
            return true;
        }

        // Over the cap. Everything so far is flushed synchronously here; the async path drains
        // before its own write, and this one is only reached from the synchronous Write.
        Drain();
        return false;
    }

    private void Drain()
    {
        PassedThrough = true;
        if (_buffer is { Length: > 0 })
        {
            inner.Write(_buffer.GetBuffer().AsSpan(0, (int)_buffer.Length));
        }

        _buffer = null;
    }

    private async ValueTask DrainAsync(CancellationToken ct)
    {
        if (_buffer is { Length: > 0 })
        {
            await inner.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), ct);
        }

        PassedThrough = true;
        _buffer = null;
    }

    public override void Flush()
    {
        if (PassedThrough)
        {
            inner.Flush();
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken) =>
        PassedThrough ? inner.FlushAsync(cancellationToken) : Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        base.Dispose(disposing);
    }
}
