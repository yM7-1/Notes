using Notes.Core.Documents;
using Xunit;

namespace Notes.Tests;

public class AnnotationProtocolTests
{
    [Fact]
    public void InsertRef_RoundTripsParts()
    {
        var refId = AnnotationProtocol.InsertRef("火炬头", "pile_draw", "cards.strike");

        Assert.True(AnnotationProtocol.IsInsert(refId));
        var parts = AnnotationProtocol.Split(refId[AnnotationProtocol.Insert.Length..]);
        Assert.Equal(new[] { "火炬头", "pile_draw", "cards.strike" }, parts);
    }

    [Fact]
    public void Join_Split_UseUnitSeparator()
    {
        var joined = AnnotationProtocol.Join("a", "b");

        Assert.Equal("a\u001fb", joined);
        Assert.Equal(new[] { "a", "b" }, AnnotationProtocol.Split(joined));
    }

    [Fact]
    public void PrefixHelpers_ClassifyRefs()
    {
        Assert.True(AnnotationProtocol.IsExhaust(AnnotationProtocol.Exhaust + "x"));
        Assert.True(AnnotationProtocol.IsDiscard(AnnotationProtocol.Discard + "x"));
        Assert.True(AnnotationProtocol.IsLoss(AnnotationProtocol.Loss));
        Assert.True(AnnotationProtocol.IsDamage(AnnotationProtocol.DamageRef("enemy")));
        Assert.True(AnnotationProtocol.IsRelic(AnnotationProtocol.RelicRef("relic")));
        Assert.True(AnnotationProtocol.IsCard(AnnotationProtocol.Card + "card"));
        Assert.True(AnnotationProtocol.IsBoundary(AnnotationProtocol.InsertRef("e", "p", "c")));
        Assert.False(AnnotationProtocol.IsBoundary(AnnotationProtocol.DamageRef("enemy")));
    }
}
