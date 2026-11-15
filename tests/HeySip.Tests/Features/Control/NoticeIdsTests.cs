using HeySip.Core.Features.Control;

namespace HeySip.Tests.Features.Control;

public sealed class NoticeIdsTests
{
    [Fact]
    public void An_update_id_names_the_version_with_three_parts() =>
        Assert.Equal("update-0.3.0", NoticeIds.Update(new Version(0, 3, 0, 7)));

    [Theory]
    [InlineData("unsupported", true)]
    [InlineData("update-1.2.3", true)]
    [InlineData("maintenance-window", false)]
    [InlineData("", false)]
    public void Only_the_apps_own_upgrade_notices_prompt_a_download(string id, bool expected) =>
        Assert.Equal(expected, NoticeIds.IsUpgradePrompt(id));

    [Fact]
    public void The_backoff_grows_and_the_schedule_constants_are_sane()
    {
        Assert.Equal(ControlService.Backoff.Order().ToList(), ControlService.Backoff.ToList());
        Assert.Equal(TimeSpan.FromDays(7), ControlService.Backoff[^1]);
        Assert.True(ControlService.DueLookEvery <= ControlService.CheckEvery);
        Assert.True(ControlService.StartupDelay < ControlService.DueLookEvery);
    }
}
