using ProvingGrounds.Core;

namespace ProvingGrounds.Tests.Core;

// Hand cases for Core/ArenaMedicalPolicy.cs (task 1.5); each expected value is read off the C++.

public class ArenaMedicalPolicyTests
{
    [Theory]   // ArenaMedicalPolicy.cpp:5-16, every input, read off the C++
    //          protocol (1 Ringside, 2 Blood), matchActive, participant, eliminated, blocked
    [InlineData(1, false, false, false, false)]
    [InlineData(1, false, false, true, false)]
    [InlineData(1, false, true, false, false)]
    [InlineData(1, false, true, true, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(1, true, false, true, false)]
    [InlineData(1, true, true, false, true)]
    [InlineData(1, true, true, true, false)]
    [InlineData(2, false, false, false, false)]
    [InlineData(2, false, false, true, false)]
    [InlineData(2, false, true, false, false)]
    [InlineData(2, false, true, true, false)]
    [InlineData(2, true, false, false, false)]
    [InlineData(2, true, false, true, false)]
    [InlineData(2, true, true, false, true)]
    [InlineData(2, true, true, true, true)]
    [InlineData(0, true, true, true, false)]   // out of range: only Bloodsport is tested by name, so it acts as Ringside Aid
    [InlineData(0, true, true, false, true)]
    public void ShouldBlockTreatment_truth_table(int protocol, bool match, bool participant, bool eliminated, bool blocked)
    {
        Assert.Equal(blocked, ArenaMedicalPolicy.ShouldBlockTreatment((ArenaMedicalPolicy.Protocol)protocol, match, participant, eliminated));
    }

    [Fact]   // ArenaMedicalPolicy.cpp:18-38; ArenaMedical.h:7-11
    public void Protocol_names_and_cycle()
    {
        Assert.Equal(1, (int)ArenaMedicalPolicy.Protocol.RingsideAid);
        Assert.Equal(2, (int)ArenaMedicalPolicy.Protocol.Bloodsport);
        Assert.True(ArenaMedicalPolicy.ShouldStabilizePrisoner(ArenaMedicalPolicy.Protocol.RingsideAid));
        Assert.False(ArenaMedicalPolicy.ShouldStabilizePrisoner(ArenaMedicalPolicy.Protocol.Bloodsport));
        Assert.True(ArenaMedicalPolicy.ShouldStabilizePrisoner((ArenaMedicalPolicy.Protocol)7));
        Assert.Equal("Bloodsport", ArenaMedicalPolicy.GetProtocolName(ArenaMedicalPolicy.Protocol.Bloodsport));
        Assert.Equal("Ringside Aid", ArenaMedicalPolicy.GetProtocolName(ArenaMedicalPolicy.Protocol.RingsideAid));
        Assert.Equal("Ringside Aid", ArenaMedicalPolicy.GetProtocolName((ArenaMedicalPolicy.Protocol)0));
        Assert.Equal(ArenaMedicalPolicy.Protocol.Bloodsport, ArenaMedicalPolicy.NextProtocol(ArenaMedicalPolicy.Protocol.RingsideAid));
        Assert.Equal(ArenaMedicalPolicy.Protocol.RingsideAid, ArenaMedicalPolicy.NextProtocol(ArenaMedicalPolicy.Protocol.Bloodsport));
        Assert.Equal(ArenaMedicalPolicy.Protocol.RingsideAid, ArenaMedicalPolicy.NextProtocol((ArenaMedicalPolicy.Protocol)0));
    }
}
