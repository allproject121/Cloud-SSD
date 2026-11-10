using CloudPlayX.App;
using Xunit;
namespace CloudPlayX.Tests;
public class SystemTelemetryTests
{
    [Fact]
    public void Reports_cpu_and_ram_on_any_windows_pc()
    {
        using var t = new SystemTelemetry();
        t.Sample();
        Thread.Sleep(600);
        var s = t.Sample();
        Assert.True(s.RamUsedPercent is > 0 and <= 100, $"ram={s.RamUsedPercent}");
        Assert.True(s.CpuLoadPercent is >= 0 and <= 100, $"cpu={s.CpuLoadPercent}");
        Assert.True(s.GpuLoadPercent is null or (>= 0 and <= 100), $"gpu={s.GpuLoadPercent}");
    }
}