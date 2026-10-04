namespace Nodalis.Infrastructure.Reliability;

public sealed record FileRevision(
    long Length,
    DateTime LastWriteUtc,
    string Sha256);
