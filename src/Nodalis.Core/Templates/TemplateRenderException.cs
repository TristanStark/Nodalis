namespace Nodalis.Core.Templates;

public sealed class TemplateRenderException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="TemplateRenderException"/>.
    /// </summary>
    /// <param name="message">The <c>message</c> value.</param>
    public TemplateRenderException(string message)
            : base(message)
    {
    }
}
