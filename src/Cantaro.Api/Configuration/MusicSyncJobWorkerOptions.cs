using System.ComponentModel.DataAnnotations;

namespace Cantaro.Api.Configuration;

public sealed class MusicSyncJobWorkerOptions
{
    public const string SectionName = "MusicSyncJobWorker";

    [Range(1, 3600)]
    public int IdleDelaySeconds { get; set; } = 60;

    public TimeSpan IdleDelay => TimeSpan.FromSeconds(IdleDelaySeconds);
}
