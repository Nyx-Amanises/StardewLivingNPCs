using Xunit;
using ValleyTalk;

namespace ValleyTalk.Tests;

public sealed class ContextRoutingPlanTests
{
    [Fact]
    public void ConservativeBriefKeepsHardBoundaryModules()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();

        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.NpcProfile));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.DateTime));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Location));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.LivingNpc));
        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.CurrentConversation));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Relationship));
        Assert.Equal(ContextDetail.None, plan.Get(ContextModule.SampleDialogue));
    }

    [Fact]
    public void GiftDependencyPromotesRelationshipAndLivingNpcContext()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();
        plan.Set(ContextModule.LivingNpc, ContextDetail.None);
        plan.Set(ContextModule.Relationship, ContextDetail.None);
        plan.Set(ContextModule.Gift, ContextDetail.Full);

        plan.ApplyDependencies();

        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.Gift));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.LivingNpc));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Relationship));
    }

    [Fact]
    public void FullLocationPromotesSceneDependencies()
    {
        var plan = ContextRoutingPlan.ConservativeBrief();
        plan.Set(ContextModule.DateTime, ContextDetail.None);
        plan.Set(ContextModule.Weather, ContextDetail.None);
        plan.Set(ContextModule.Location, ContextDetail.Full);

        plan.ApplyDependencies();

        Assert.Equal(ContextDetail.Full, plan.Get(ContextModule.Location));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.DateTime));
        Assert.Equal(ContextDetail.Brief, plan.Get(ContextModule.Weather));
    }
}