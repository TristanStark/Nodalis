using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using Nodalis.Core.Importing;

namespace Nodalis.Infrastructure.Importing;

/// <summary>
/// Inspects images embedded directly in the main Word document.
/// Enforces strict ZIP-entry, size, media type and per-document limits.
/// </summary>
public sealed class DocxInlineMediaInspector
{
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace WordProcessingDrawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    private const int MaxUniqueMedia = 256;
    private const long MaxMediaSize = 16L * 1024 * 1024;
    private const long MaxTotalMediaSize = 100L * 1024 * 1024;
    private readonly ZipArchive _archive;
    private readonly IReadOnlyDictionary<string, DocxRelationship> _relationships;
    private readonly Dictionary<string, DocxInlineMedia> _cachedMedia = new Dictionary<string, DocxInlineMedia>(StringComparer.Ordinal);
    private long _totalSize;

    /// <summary>Initializes a scanner for a single ZIP archive, without workspace access.</summary>
    public DocxInlineMediaInspector(
        ZipArchive archive,
        IReadOnlyDictionary<string, DocxRelationship> relationships)
    {
        _archive = archive;
        _relationships = relationships;
    }

    /// <summary>
    /// Finds local images and unsupported objects, keeping each one's position in a Word paragraph.
    /// </summary>
    public List<DocxInlineMedia> InspectParagraph(XElement paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        List<DocxInlineMedia> result = [];
        int offset = 0;
        foreach (XElement node in paragraph.Descendants())
        {
            if (node.Name == Word + "t")
            {
                offset += node.Value.Length;
                continue;
            }

            if (node.Name == Word + "tab")
            {
                offset++;
                continue;
            }

            if (node.Name == Word + "br" || node.Name == Word + "cr")
            {
                offset += Environment.NewLine.Length;
                continue;
            }

            if (node.Name == Drawing + "blip")
            {
                string? embeddedId = node.Attribute(Relationships + "embed")?.Value;
                string? linkedId = node.Attribute(Relationships + "link")?.Value;
                string altText = ReadAlternativeText(node);
                if (string.IsNullOrWhiteSpace(embeddedId))
                {
                    result.Add(Unsupported(linkedId ?? "image liée", offset, altText,
                        "Image Word liée : téléchargement externe interdit ; conserver le DOCX source."));
                    continue;
                }

                DocxInlineMedia image = ResolveImage(embeddedId);
                result.Add(image with { TextOffset = offset, AlternativeText = altText });
                continue;
            }

            if (node.Name.LocalName is "OLEObject" or "object" or "pict")
            {
                // A pict may wrap Word VML, and object/OLE may expose executable content.
                // Show one warning for the enclosing unsupported structure.
                if (!node.Ancestors().Any(ancestor => ancestor.Name.LocalName is "pict" or "object"))
                {
                    result.Add(Unsupported(node.Name.LocalName, offset, string.Empty,
                        "Objet Word non convertible (OLE/VML/forme) : ressource originale conservée dans le DOCX, jamais exécutée."));
                }
                continue;
            }

            if (node.Name.LocalName == "graphicData" &&
                (node.Attribute("uri")?.Value.Contains("diagram", StringComparison.OrdinalIgnoreCase) == true))
            {
                result.Add(Unsupported("SmartArt", offset, string.Empty,
                    "SmartArt Word : conversion automatique non disponible ; consulter le DOCX d'origine."));
            }
        }

        return result;
    }

    /// <summary>Builds a warning-only placeholder for unavailable Word content.</summary>
    private static DocxInlineMedia Unsupported(string id, int position, string alt, string warning) =>
        new DocxInlineMedia
        {
            SourceId = id,
            TextOffset = position,
            AlternativeText = alt,
            Warning = warning
        };

    /// <summary>Extracts a non-executable alternative text label from a drawing's docPr element.</summary>
    private static string ReadAlternativeText(XElement blip)
    {
        XElement? drawing = blip.Ancestors()
            .FirstOrDefault(node => node.Name == Word + "drawing");
        XElement? documentProperty = drawing?.Descendants(WordProcessingDrawing + "docPr")
            .FirstOrDefault();
        string alt = documentProperty?.Attribute("descr")?.Value ??
                     documentProperty?.Attribute("title")?.Value ??
                     documentProperty?.Attribute("name")?.Value ??
                     "Image Word";
        return alt.Length <= 200 ? alt : alt[..200];
    }

    /// <summary>Validates the local ZIP relationship once and calculates a bounded content hash.</summary>
    private DocxInlineMedia ResolveImage(string relationId)
    {
        if (_cachedMedia.TryGetValue(relationId, out DocxInlineMedia? cached))
        {
            return cached;
        }

        if (_cachedMedia.Count >= MaxUniqueMedia)
        {
            throw new InvalidDataException("Le nombre d'images DOCX dépasse la limite de sécurité.");
        }

        DocxInlineMedia resolved;
        if (!_relationships.TryGetValue(relationId, out DocxRelationship? relationship) ||
            relationship.IsExternal)
        {
            resolved = Unsupported(relationId, 0, string.Empty,
                "Image Word externe ou relation absente : aucune ressource téléchargée.");
        }
        else if (!relationship.Type.EndsWith("/image", StringComparison.OrdinalIgnoreCase))
        {
            resolved = Unsupported(relationId, 0, string.Empty,
                "Objet Word sans relation image interne exploitable.");
        }
        else
        {
            string target = relationship.Target.Replace('\\', '/');
            if (target.StartsWith('/') || target.Contains("..", StringComparison.Ordinal) ||
                target.Contains(':') || target.Contains('?') || target.Contains('#'))
            {
                resolved = Unsupported(relationId, 0, string.Empty,
                    "Chemin de média Word dangereux : copie refusée.");
            }
            else
            {
                string entryPath = target.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)
                    ? target : "word/" + target;
                string extension = Path.GetExtension(entryPath).ToLowerInvariant();
                if (!entryPath.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase) ||
                    extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp"))
                {
                    resolved = Unsupported(relationId, 0, string.Empty,
                        "Format ou emplacement de média Word non pris en charge : DOCX original conservé.");
                }
                else
                {
                    ZipArchiveEntry? entry = _archive.GetEntry(entryPath);
                    if (entry is null)
                    {
                        resolved = Unsupported(relationId, 0, string.Empty,
                            "Image référencée mais absente du paquet DOCX.");
                    }
                    else
                    {
                        if (entry.Length > MaxMediaSize ||
                            _totalSize > MaxTotalMediaSize - entry.Length ||
                            entry.CompressedLength > 0 &&
                            entry.Length > entry.CompressedLength * 250L)
                        {
                            throw new InvalidDataException("Image DOCX trop volumineuse ou compression suspecte.");
                        }

                        string hash = HashArchiveEntry(entry);
                        _totalSize += entry.Length;
                        string filename = hash + extension;
                        resolved = new DocxInlineMedia
                        {
                            SourceId = relationId,
                            ArchiveEntryPath = entry.FullName,
                            Sha256 = hash,
                            MarkdownRelativePath = "../Media-Docx/" + filename
                        };
                    }
                }
            }
        }

        _cachedMedia.Add(relationId, resolved);
        return resolved;
    }

    /// <summary>Hashes untrusted ZIP data with an actual enforced output byte limit.</summary>
    private static string HashArchiveEntry(ZipArchiveEntry entry)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using Stream stream = entry.Open();
        byte[] buffer = new byte[81920];
        long readTotal = 0;
        int bytes;
        while ((bytes = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            readTotal += bytes;
            if (readTotal > MaxMediaSize)
            {
                throw new InvalidDataException("Image DOCX décompressée trop volumineuse.");
            }

            hash.AppendData(buffer, 0, bytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
