using System.IO;
using System.Linq;
using PlcDbSnapshot.Core;
using Xunit;

public class SnapshotTests
{
    private static Snapshot Make(params (string tag, string type, string value, string status)[] rows)
    {
        var s = new Snapshot();
        s.Metadata[Snapshot.KeyPlc] = "10.0.0.1";
        foreach (var r in rows) s.Entries.Add(new SnapshotEntry(r.tag, r.type, r.value, r.status));
        return s;
    }

    [Theory]
    [InlineData("MyDb.A.B", "MyDb")]
    [InlineData("ArrDb[3].X", "ArrDb")]
    [InlineData("\"Odd.Name\".X", "Odd.Name")]
    [InlineData("Plain", "Plain")]
    public void BlockOf_returns_first_segment(string tag, string expected)
    {
        Assert.Equal(expected, TagPath.BlockOf(tag));
    }

    [Fact]
    public void Csv_round_trip_keeps_metadata_and_awkward_values()
    {
        var s = Make(("Db.Text", "String", "a,\"b\" c", "OK"), ("Db.N", "Int", "-5", "OK"), ("Db.Bad", "Real", "", "읽기 실패 5"));
        s.Metadata[Snapshot.KeyNote] = "교체 전";
        var sw = new StringWriter();
        SnapshotCsv.Write(s, sw);
        var back = SnapshotCsv.Read(new StringReader(sw.ToString()));

        Assert.Equal("10.0.0.1", back.Get(Snapshot.KeyPlc));
        Assert.Equal("교체 전", back.Get(Snapshot.KeyNote));
        Assert.Equal(3, back.Entries.Count);
        Assert.Equal("a,\"b\" c", back.Entries[0].Value);
        Assert.False(back.Entries[2].IsOk);
    }

    [Fact]
    public void Read_rejects_non_snapshot_file()
    {
        Assert.Throws<InvalidDataException>(() => SnapshotCsv.Read(new StringReader("x,y\n1,2\n")));
    }

    [Fact]
    public void Compare_classifies_rows()
    {
        var a = Make(("D.Same", "Int", "1", "OK"), ("D.Chg", "Int", "1", "OK"), ("D.OnlyA", "Bool", "True", "OK"), ("D.Err", "Int", "", "x"));
        var b = Make(("D.Same", "Int", "1", "OK"), ("D.Chg", "Int", "2", "OK"), ("D.OnlyB", "Bool", "False", "OK"), ("D.Err", "Int", "3", "OK"));
        var r = SnapshotComparer.Compare(a, b);

        Assert.Equal(1, r.Same);
        Assert.Equal(1, r.Changed);
        Assert.Equal(1, r.OnlyInA);
        Assert.Equal(1, r.OnlyInB);
        Assert.Equal(1, r.ReadErrors);
        Assert.DoesNotContain(r.Rows, x => x.Kind == DiffKind.Same);
        Assert.Equal("2", r.Rows.Single(x => x.Kind == DiffKind.Changed).ValueB);
    }

    [Fact]
    public void Compare_honours_ignore_regex_and_real_tolerance()
    {
        var a = Make(("D.Pos", "Real", "10.001", "OK"), ("D.Timer.ET", "Time", "1s", "OK"));
        var b = Make(("D.Pos", "Real", "10.002", "OK"), ("D.Timer.ET", "Time", "2s", "OK"));
        var r = SnapshotComparer.Compare(a, b, new CompareOptions { IgnoreTagRegex = @"\.ET$", RealTolerance = 0.01 });

        Assert.Equal(1, r.Ignored);
        Assert.Equal(1, r.Same);
        Assert.Empty(r.Rows);
    }
}
