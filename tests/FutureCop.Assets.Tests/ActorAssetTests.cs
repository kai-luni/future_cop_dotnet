using System.Text;
using FutureCop.Assets;
using Xunit;
namespace FutureCop.Assets.Tests;
public class ActorAssetTests
{
    private static void Tag(BinaryWriter w, string name) => w.Write(Encoding.ASCII.GetBytes(new string(name.Reverse().ToArray())));
    [Fact]
    public void ParsesPlayerActorAndResourceSlots()
    {
        using var m = new MemoryStream();
        using (var w = new BinaryWriter(m, Encoding.UTF8, true))
        {
            Tag(w,"tACT"); w.Write(48); w.Write(77); w.Write(new byte[] {1,0,0,0}); w.Write(new byte[32]);
            Tag(w,"aRSL"); w.Write(60); w.Write(77);
            foreach (int id in new[] {11,0,12,13,14,15}) {Tag(w,id == 0 ? "NULL" : "Cobj"); w.Write(id);}
        }
        string path = System.IO.Path.GetTempFileName();
        try {
            File.WriteAllBytes(path,m.ToArray());
            var actor = Assert.Single(new MisFile(path).PlayerActors);
            Assert.Equal(77,actor.Id);
            Assert.Equal(new[] {11,0,12,13,14,15},actor.Resources.Select(x => x.Id));
        } finally {File.Delete(path);}
    }
}
