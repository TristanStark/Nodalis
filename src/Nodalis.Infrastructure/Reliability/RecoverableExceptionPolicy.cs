using System.Runtime.InteropServices;
using System.Text.Json;

namespace Nodalis.Infrastructure.Reliability;

/// <summary>
/// Classifies exceptions that can safely stop one user action without forcing the Nodalis process to terminate.
/// </summary>
public static class RecoverableExceptionPolicy
{
    /// <summary>
    /// Determines whether the current process can continue after the supplied exception.
    /// </summary>
    /// <param name="exception">The exception raised by a UI or background action.</param>
    /// <returns><see langword="true"/> only for known action-scoped failures.</returns>
    public static bool CanContinue(
            Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        if (IsProcessFatal(
                exception))
        {
            return false;
        }

        if (exception is AggregateException aggregateException)
        {
            IReadOnlyList<Exception> innerExceptions =
                aggregateException
                    .Flatten()
                    .InnerExceptions;

            return innerExceptions.Count >
                       0 &&
                   innerExceptions.All(
                       CanContinue);
        }

        if (exception is OperationCanceledException or
            IOException or
            UnauthorizedAccessException or
            InvalidDataException or
            InvalidOperationException or
            ArgumentException or
            IndexOutOfRangeException or
            NullReferenceException or
            KeyNotFoundException or
            FormatException or
            TimeoutException or
            NotSupportedException or
            JsonException or
            System.Text.DecoderFallbackException or
            System.ComponentModel.Win32Exception or
            ExternalException)
        {
            return true;
        }

        string? exceptionType =
            exception.GetType().FullName;

        if (string.Equals(
                exceptionType,
                "System.Windows.Markup.XamlParseException",
                StringComparison.Ordinal) ||
            string.Equals(
                exceptionType,
                "Nodalis.Core.Validation.DomainValidationException",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(
                exceptionType,
                "System.Reflection.TargetInvocationException",
                StringComparison.Ordinal) &&
            exception.InnerException is Exception innerException)
        {
            return CanContinue(
                innerException);
        }

        return false;
    }

    /// <summary>
    /// Returns a short functional message suitable for the primary error UI.
    /// </summary>
    /// <param name="exception">The captured recoverable exception.</param>
    /// <returns>A user-facing message without a raw stack trace.</returns>
    public static string GetUserMessage(
            Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        if (exception is OperationCanceledException)
        {
            return "L'action a été annulée.";
        }

        if (exception is FileNotFoundException or
            DirectoryNotFoundException)
        {
            return "Un fichier ou dossier utilisé par cette action n'est plus disponible.";
        }

        if (exception is UnauthorizedAccessException)
        {
            return "Nodalis n'a pas l'autorisation nécessaire pour terminer cette action.";
        }

        if (exception is IOException)
        {
            return "Une opération sur les fichiers n'a pas pu être terminée.";
        }

        if (exception is ArgumentException or
            IndexOutOfRangeException or
            NullReferenceException or
            InvalidOperationException or
            KeyNotFoundException)
        {
            return "L'état de l'interface ou des données a changé pendant l'action.";
        }

        if (string.Equals(
                exception.GetType().FullName,
                "System.Windows.Markup.XamlParseException",
                StringComparison.Ordinal))
        {
            return "Une vue Nodalis n'a pas pu être chargée correctement.";
        }

        return "L'action n'a pas pu être terminée.";
    }

    /// <summary>
    /// Identifies runtime failures for which Nodalis must not pretend that continuing is safe.
    /// </summary>
    /// <param name="exception">The exception to classify.</param>
    /// <returns><see langword="true"/> when the process state may no longer be reliable.</returns>
    public static bool IsProcessFatal(
            Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        return exception is
            OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            SEHException;
    }
}
