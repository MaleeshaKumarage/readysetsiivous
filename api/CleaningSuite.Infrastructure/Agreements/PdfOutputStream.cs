namespace CleaningSuite.Infrastructure.Agreements;

// QuestPDF's Stream overload writes only the current attempt. Enforce the cap
// before every filesystem allocation, including seeks and SetLength.
internal sealed class PdfOutputStream(Stream inner, long maximum) : Stream
{
    private void Check(long length)
    { if (length < 0 || length > maximum) throw new FluentValidation.ValidationException("Signed PDF exceeds the output budget."); }
    public override bool CanRead => false;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => true;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set { Check(value); inner.Position = value; } }
    public override void Flush() => inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin)
    {
        var target = checked((origin == SeekOrigin.Begin ? 0 : origin == SeekOrigin.Current ? inner.Position : inner.Length) + offset);
        Check(target); return inner.Seek(offset, origin);
    }
    public override void SetLength(long value) { Check(value); inner.SetLength(value); }
    public override void Write(byte[] buffer, int offset, int count)
    { Check(checked(inner.Position + count)); inner.Write(buffer, offset, count); }
    public override void Write(ReadOnlySpan<byte> buffer)
    { Check(checked(inner.Position + buffer.Length)); inner.Write(buffer); }
    protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
}
