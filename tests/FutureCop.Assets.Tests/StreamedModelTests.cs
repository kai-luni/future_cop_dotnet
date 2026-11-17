using System.Text;
using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class StreamedModelTests
{
    private static void Word(BinaryWriter w, int value) => w.Write(value);
    private static void Tag(BinaryWriter w, string name) => w.Write(Encoding.ASCII.GetBytes(new string(name.Reverse().ToArray())));
    private static void Chunk(BinaryWriter w, string name, int index, byte[] data)
    {
        Tag(w, name); Word(w, data.Length + 12); Word(w, index); w.Write(data);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 16)]
    [InlineData(true, 16)]
    public void ModelChunksCanSpanStreamPackets(bool startsAfterFill, int extraHeaderBytes)
    {
        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload, Encoding.UTF8, true))
        {
            Chunk(w, "4DGI", 1, BitConverter.GetBytes(0x02000000));
            var hierarchy = new byte[20]; hierarchy[0] = 7;
            Chunk(w, "3DHY", 1, hierarchy);
            Chunk(w, "3DHS", 2, new byte[] { 1,0,2,0,3,0,0,0, 4,0,5,0,6,0,0,0 });
            Chunk(w, "AnmD", 1, new byte[] { 0,0,0,0, 7,0,9,0, 0,0,0,0, 0,0,0,0 });
        }
        var bytes = payload.ToArray();
        using var archive = new MemoryStream();
        using (var w = new BinaryWriter(archive, Encoding.UTF8, true))
        {
            Tag(w, "SHOC"); Word(w, 60 + extraHeaderBytes); Word(w, 0); Word(w, 0); Tag(w, "SHDR");
            Word(w, 2); Tag(w, "Cobj"); Word(w, 11); Word(w, bytes.Length);
            for (int i = 0; i < 6; i++) Word(w, 0);
            w.Write(new byte[extraHeaderBytes]);
            if (startsAfterFill)
            {
                Tag(w, "FILL"); Word(w, 16); Word(w, 0); Word(w, 0);
                w.Write(new byte[0x6000 - (int)archive.Position]);
            }
            int first = 28;
            Tag(w, "SHOC"); Word(w, first + 20); Word(w, 0); Word(w, 0); Tag(w, "SDAT");
            w.Write(bytes, 0, first);
            Tag(w, "FILL"); Word(w, 16); Word(w, 0); Word(w, 0);
            w.Write(new byte[(int)(((archive.Position / 0x6000) + 1) * 0x6000 - archive.Position)]);
            Tag(w, "SHOC"); Word(w, bytes.Length - first + 20); Word(w, 0); Word(w, 0); Tag(w, "SDAT");
            w.Write(bytes, first, bytes.Length - first);
        }
        string path = System.IO.Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, archive.ToArray());
            var model = Assert.Single(new MisFile(path).Models);
            Assert.Equal(11, model.Id);
            Assert.Single(model.Hierarchy);
            Assert.Equal(7, model.Hierarchy[0].MatrixIndex);
            Assert.Equal(new Vertex(4,5,6), model.AttachmentPoints[1]);
            Assert.Equal(2, model.AttachmentPointCount);
            Assert.Equal(7, model.AnimationStarts[0]);
        }
        finally { File.Delete(path); }
    }
}
