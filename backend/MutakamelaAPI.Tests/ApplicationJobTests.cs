using MutakamelaAPI.Models;
using Xunit;

namespace MutakamelaAPI.Tests;

public class ApplicationJobTests
{
    [Fact]
    public void Cannot_skip_straight_to_submitting_or_done()
    {
        var job = new ApplicationJob { Id = "sm" };
        Assert.False(job.TryTransition(JobStatus.Submitting));
        Assert.False(job.TryTransition(JobStatus.Done));
        Assert.Equal(JobStatus.Collecting, job.Status);
    }

    [Fact]
    public void Happy_path_transitions_are_allowed_and_done_is_terminal()
    {
        var job = new ApplicationJob { Id = "sm" };
        foreach (var next in new[] { JobStatus.Validating, JobStatus.AwaitingLogin, JobStatus.Filling, JobStatus.AwaitingApproval, JobStatus.Submitting, JobStatus.Done })
            Assert.True(job.TryTransition(next), $"expected transition to {next}");
        Assert.False(job.TryTransition(JobStatus.Collecting));
        Assert.True(job.IsTerminal);
        Assert.Contains(job.Events, e => e.Type == "status" && e.Message.Contains("Submitting → Done"));
    }

    [Fact]
    public void Approval_can_send_the_job_back_to_collecting_for_edits()
    {
        var job = new ApplicationJob { Id = "sm", Status = JobStatus.AwaitingApproval };
        Assert.True(job.TryTransition(JobStatus.Collecting, "edit"));
    }
}
