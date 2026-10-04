namespace Nodalis.Infrastructure.Reliability;

public sealed class ExternalModificationException : IOException
{
    public ExternalModificationException(string path)
        : base($"File '{path}' was modified outside Nodalis after it was opened.")
    {
        Path = path;
    }

    public string Path { get; }
}
