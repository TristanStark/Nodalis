namespace Nodalis.Core.Validation;

public sealed class DomainValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of <see cref="DomainValidationException"/>.
    /// </summary>
    /// <param name="message">The <c>message</c> value.</param>
    public DomainValidationException(string message)
            : base(message)
    {
    }
}
