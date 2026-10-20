using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Rejects unsafe Open XML packages before parsing or copying any embedded media.
/// No external XML entities, executable ZIP members or filesystem extraction are permitted.
/// </summary>
internal static class DocxPackageGuard
{
    private const int MaxEntries = 4096;
    private const long MaxTotalUncompressed = 256L * 1024 * 1024;
    private const long MaxXmlEntry = 32L * 1024 * 1024;
    private const long MaxMediaEntry = 16L * 1024 * 1024;
    private const long MaxOtherEntry = 64L * 1024 * 1024;
    private const long MaxCompressionRatio = 1000;

    /// <summary>
    /// Validates all ZIP members, including those not referenced by the selected Word sections.
    /// </summary>
    public static void Validate(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("DOCX refusé : trop d'entrées dans le paquet ZIP.");
        }

        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long uncompressed = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = entry.FullName;
            string[] segments = path.Split('/');
            if (string.IsNullOrWhiteSpace(path) ||
                path.StartsWith('/') ||
                path.Contains('\\') ||
                path.Contains(':') ||
                path.IndexOf('\0') >= 0 ||
                segments.Any(segment => segment is "." or "..") ||
                segments.Take(segments.Length - 1).Any(string.IsNullOrEmpty) ||
                (!path.EndsWith('/') && string.IsNullOrEmpty(segments[^1])) ||
                !names.Add(path))
            {
                throw new InvalidDataException("DOCX refusé : chemin ZIP ambigu, dangereux ou dupliqué.");
            }

            // Unix ZIP permissions occupy the high 16 bits. Forbid symlinks and device nodes.
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000))
            {
                throw new InvalidDataException("DOCX refusé : entrée ZIP de type fichier spécial.");
            }

            bool directory = path.EndsWith('/');
            if ((directory && (entry.Length != 0 || unixType == 0x8000)) ||
                (!directory && unixType == 0x4000))
            {
                throw new InvalidDataException("DOCX refusé : métadonnées ZIP incohérentes.");
            }

            bool xml = path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                       path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);
            long limit = xml
                ? MaxXmlEntry
                : path.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                    ? MaxMediaEntry
                    : MaxOtherEntry;
            if (entry.Length > limit ||
                entry.Length > MaxTotalUncompressed - uncompressed)
            {
                throw new InvalidDataException("DOCX refusé : taille décompressée excessive.");
            }

            if (entry.Length > 0 &&
                (entry.CompressedLength == 0 ||
                 (entry.Length > 1024L * 1024 &&
                  entry.Length / entry.CompressedLength > MaxCompressionRatio)))
            {
                throw new InvalidDataException("DOCX refusé : compression ZIP anormale.");
            }

            uncompressed += entry.Length;
        }
    }

    /// <summary>
    /// Reads a bounded XML part without DTD processing or external resource resolution.
    /// The ZIP metadata limit is complemented by an actual parsed-character limit.
    /// </summary>
    public static XDocument LoadXml(Stream stream, LoadOptions options = LoadOptions.None)
    {
        ArgumentNullException.ThrowIfNull(stream);
        XmlReaderSettings settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlEntry,
            MaxCharactersFromEntities = 1024,
            CloseInput = false
        };

        using XmlReader reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, options);
    }
}
