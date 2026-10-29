namespace Forensic.Cliente.Tests.Dobles;

/// <summary>Un destino de descarga en memoria que sobrevive a que el código bajo prueba lo cierre.</summary>
internal sealed class DestinoEnMemoria : Stream
{
    private readonly MemoryStream _bytes = new();

    public byte[] ToArray() => _bytes.ToArray();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _bytes.Length;
    public override long Position { get => _bytes.Position; set => throw new NotSupportedException(); }
    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => _bytes.Write(buffer, offset, count);
}
