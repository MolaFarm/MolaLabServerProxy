using System;

namespace ServerProxy.Tools;

public class VersionInfo
{
    public string? CommitSha { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string? DownloadAddress { get; set; }
}