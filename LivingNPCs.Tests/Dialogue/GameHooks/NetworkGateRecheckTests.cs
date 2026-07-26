using System.Threading.Tasks;
using LivingNPCs.Dialogue;
using LivingNPCs.Dialogue.GameHooks;
using LivingNPCs.Dialogue.Llm;
using Xunit;

namespace LivingNPCs.Tests.Dialogue.GameHooks;

/// <summary>
/// F7 修复验证：后台重试探测的成功结果真正参与放行——Android 即时接口误报 false 时，
/// 只要后台 HTTP 实测确认过可用就放行本次生成并触发复核；复核失败则翻回拦截（双向自愈）。
/// </summary>
[Collection("LlmLayer")]
public sealed class NetworkGateRecheckTests : System.IDisposable
{
    public NetworkGateRecheckTests()
    {
        DialogueServices.Initialize(null!, null!, new DialogueConfig());
        NetworkGate.ResetForTests();
    }

    public void Dispose()
    {
        NetworkGate.ResetForTests();
        AndroidPlatform.OverrideForTests = null;
    }

    [Fact]
    public async Task Background_Success_Grants_Availability_Despite_Instant_Misreport()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkGate.InstantProbeForTests = () => false;
        NetworkGate.BackgroundProbeForTests = () => Task.FromResult(true);

        // 从未验证过：首查失败 → 拦截，并踢出后台探测。
        Assert.False(NetworkGate.IsAvailableForGeneration());
        Assert.NotNull(NetworkGate.LastBackgroundProbe);
        await NetworkGate.LastBackgroundProbe!;

        // 后台已确认可用：即时探测仍误报 false 也放行（F7 核心语义）。
        Assert.True(NetworkGate.IsAvailableForGeneration());
    }

    [Fact]
    public async Task Failed_Recheck_Flips_Back_To_Blocking()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkGate.InstantProbeForTests = () => false;
        NetworkGate.BackgroundProbeForTests = () => Task.FromResult(true);

        Assert.False(NetworkGate.IsAvailableForGeneration());
        await NetworkGate.LastBackgroundProbe!;

        // 网络真断了：复核开始失败。放行的那一次会触发复核……
        NetworkGate.BackgroundProbeForTests = () => Task.FromResult(false);
        Assert.True(NetworkGate.IsAvailableForGeneration());
        await NetworkGate.LastBackgroundProbe!;

        // ……复核失败后恢复拦截。
        Assert.False(NetworkGate.IsAvailableForGeneration());
    }

    [Fact]
    public void Instant_Success_Also_Seeds_The_Cache()
    {
        AndroidPlatform.OverrideForTests = true;
        bool instantAvailable = true;
        NetworkGate.InstantProbeForTests = () => instantAvailable;
        NetworkGate.BackgroundProbeForTests = () => Task.FromResult(true);

        Assert.True(NetworkGate.IsAvailableForGeneration());

        // 即时探测转为误报 false：上一次真实成功的信号仍给予放行（并触发后台复核）。
        instantAvailable = false;
        Assert.True(NetworkGate.IsAvailableForGeneration());
    }

    [Fact]
    public void Fresh_State_Blocks_Until_Any_Successful_Verification()
    {
        AndroidPlatform.OverrideForTests = true;
        NetworkGate.InstantProbeForTests = () => false;
        NetworkGate.BackgroundProbeForTests = () => Task.FromResult(false);

        Assert.False(NetworkGate.IsAvailableForGeneration());
        Assert.False(NetworkGate.IsAvailableForGeneration());
    }
}
