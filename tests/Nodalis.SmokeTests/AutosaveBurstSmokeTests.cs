using Nodalis.Infrastructure.Reliability;

/// <summary>
/// Protects flush and conflict invariants when many editor tabs save at once.
/// </summary>
internal static class AutosaveBurstSmokeTests
{
    /// <summary>
    /// Simulates a large collection of pending editors and explicit bulk-close
    /// preflight without relying on fragile timing thresholds.
    /// </summary>
    /// <param name="root">Isolated smoke-test workspace root.</param>
    /// <returns>The completed reliability verification.</returns>
    public static async Task RunAsync(string root)
    {
        string directory = Path.Combine(root, "AutosaveBurst");
        Directory.CreateDirectory(directory);

        List<(string Path, TextDocumentSession Session, DocumentAutosaveController Autosave)> tabs = [];
        int saveEvents = 0;
        for (int index = 0; index < 40; index++)
        {
            string path = Path.Combine(directory, $"Onglet-{index:D3}.md");
            await File.WriteAllTextAsync(path, "Initial");
            TextDocumentSession session = await TextDocumentSession.OpenAsync(path);
            DocumentAutosaveController autosave = new DocumentAutosaveController(
                session,
                TimeSpan.FromMinutes(5));
            autosave.Saved += (_, _) => Interlocked.Increment(ref saveEvents);

            for (int revision = 0; revision < 6; revision++)
            {
                autosave.Schedule($"Version {index:D3}-{revision:D2}");
            }

            tabs.Add((path, session, autosave));
        }

        try
        {
            // Every controller is flushed once. Replacing an editor debounce
            // many times must never cause lost data during a bulk tab close.
            foreach ((string path, TextDocumentSession session, DocumentAutosaveController autosave) in tabs)
            {
                await autosave.FlushAsync();
                await autosave.FlushAsync();
                Assert(
                    string.Equals(session.Content,
                        await File.ReadAllTextAsync(path),
                        StringComparison.Ordinal),
                    "Flushing an unchanged tab twice must preserve its exact saved content.");
            }

            Assert(saveEvents == tabs.Count,
                "Each modified tab must produce one successful write, not duplicate flushes.");
            for (int index = 0; index < tabs.Count; index++)
            {
                string expected = $"Version {index:D3}-05";
                Assert(
                    string.Equals(await File.ReadAllTextAsync(tabs[index].Path),
                        expected,
                        StringComparison.Ordinal),
                    "Bulk-close preflight must persist each tab's final revision.");
            }
        }
        finally
        {
            foreach ((string path, TextDocumentSession session, DocumentAutosaveController autosave) in tabs)
            {
                await autosave.DisposeAsync();
            }
        }

        string conflictedPath = Path.Combine(directory, "Conflit.md");
        await File.WriteAllTextAsync(conflictedPath, "Before");
        TextDocumentSession conflictedSession = await TextDocumentSession.OpenAsync(conflictedPath);
        await using (DocumentAutosaveController conflictedAutosave =
            new DocumentAutosaveController(conflictedSession, TimeSpan.FromMinutes(5)))
        {
            int conflicts = 0;
            conflictedAutosave.ConflictDetected += (_, _) =>
                Interlocked.Increment(ref conflicts);
            conflictedAutosave.Schedule("Local unsaved revision");
            await File.WriteAllTextAsync(conflictedPath, "External competing revision");
            await conflictedAutosave.FlushAsync();
            Assert(
                conflicts == 1 &&
                string.Equals(await File.ReadAllTextAsync(conflictedPath),
                    "External competing revision",
                    StringComparison.Ordinal) &&
                conflictedSession.Content == "Before",
                "A conflicting tab must never overwrite the external file during bulk close.");
        }
    }

    /// <summary>Throws on an autosave bulk-close reliability regression.</summary>
    /// <param name="condition">The invariant to check.</param>
    /// <param name="message">The diagnostic message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
