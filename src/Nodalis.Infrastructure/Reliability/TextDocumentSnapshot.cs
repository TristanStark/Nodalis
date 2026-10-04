namespace Nodalis.Infrastructure.Reliability;

public sealed record TextDocumentSnapshot(
    string Content,
    FileRevision Revision);
