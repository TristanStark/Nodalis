namespace Nodalis.Infrastructure.Reliability;

public sealed class ExternalModificationException : IOException
{
    /// <summary>
    /// Initializes a new instance of <see cref="ExternalModificationException"/>.
    /// </summary>
    /// <param name="path">The <c>path</c> value.</param>
    public ExternalModificationException(string path)
            : base($"File '{path}' was modified outside Nodalis after it was opened.")
    {
        Path = path;
    }

    public string Path { get; }
}
