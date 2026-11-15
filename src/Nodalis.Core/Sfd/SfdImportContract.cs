using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nodalis.Core.Sfd;

/// <summary>Unapplied SFD response for one of the four generation stages.</summary>
public sealed record SfdImportEnvelope
{
    public required string Schema { get; init; }
    public required string Step { get; init; }
    public required string RunId { get; init; }
    public required List<SfdImportProposal> Proposals { get; init; } = [];
}

/// <summary>Explicit proposal; no client-generated persisted GUID or path is allowed.</summary>
public sealed record SfdImportProposal
{
    public required string ClientKey { get; init; }
    public required string Kind { get; init; }
    public required string Operation { get; init; }
    public required string Certainty { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; } = string.Empty;
    public string? ParentKey { get; init; }
    public Guid? TargetId { get; init; }
    public string? UiMode { get; init; }
    public string? RegulatorySource { get; init; }
    public required List<string> SourceKeys { get; init; } = [];
    public required List<string> ActivityKeys { get; init; } = [];
    public required List<string> RequirementKeys { get; init; } = [];
    public required List<SfdImportCondition> Conditions { get; init; } = [];
}

/// <summary>One C candidate nested in its owning requirement or EMO proposal.</summary>
public sealed record SfdImportCondition
{
    public required string ClientKey { get; init; }
    public required string Statement { get; init; }
    public required List<SfdImportSubcondition> Subconditions { get; init; } = [];
}

/// <summary>One SC candidate referencing reusable RM client/existing keys.</summary>
public sealed record SfdImportSubcondition
{
    public required string ClientKey { get; init; }
    public required string Statement { get; init; }
    public required List<string> BusinessRuleKeys { get; init; } = [];
}

/// <summary>
/// First shared SFD external-response contract. Both preparer and future
/// reviewed importer must use this exact version; never treat output as approval.
/// </summary>
public static class SfdImportContract
{
    public const string Schema = "nodalis.sfd.import.v1";

    /// <summary>
    /// Exhaustive public JSON Schema shown to external models in every
    /// specialized SFD prompt, alongside the rules enforced by Parse.
    /// </summary>
    public const string JsonSchema = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "title": "Nodalis SFD import proposal v1",
          "type": "object",
          "additionalProperties": false,
          "required": ["schema", "step", "runId", "proposals"],
          "properties": {
            "schema": {"const": "nodalis.sfd.import.v1"},
            "step": {"enum": ["sfd-01", "sfd-02", "sfd-03", "sfd-04"]},
            "runId": {"$ref": "#/$defs/key"},
            "proposals": {
              "type": "array", "minItems": 1, "maxItems": 100,
              "items": {"$ref": "#/$defs/proposal"}
            }
          },
          "$defs": {
            "key": {
              "type": "string",
              "minLength": 1,
              "maxLength": 80,
              "pattern": "^[A-Za-z0-9_-]+$"
            },
            "keyList": {
              "type": "array", "maxItems": 100,
              "items": {"$ref": "#/$defs/key"},
              "uniqueItems": true
            },
            "subcondition": {
              "type": "object", "additionalProperties": false,
              "required": ["clientKey", "statement", "businessRuleKeys"],
              "properties": {
                "clientKey": {"$ref": "#/$defs/key"},
                "statement": {"type": "string", "minLength": 1, "maxLength": 1000},
                "businessRuleKeys": {"$ref": "#/$defs/keyList"}
              }
            },
            "condition": {
              "type": "object", "additionalProperties": false,
              "required": ["clientKey", "statement", "subconditions"],
              "properties": {
                "clientKey": {"$ref": "#/$defs/key"},
                "statement": {"type": "string", "minLength": 1, "maxLength": 1000},
                "subconditions": {
                  "type": "array", "maxItems": 100,
                  "items": {"$ref": "#/$defs/subcondition"}
                }
              }
            },
            "proposal": {
              "type": "object", "additionalProperties": false,
              "required": [
                "clientKey", "kind", "operation", "certainty", "title",
                "description", "sourceKeys", "activityKeys",
                "requirementKeys", "conditions"
              ],
              "properties": {
                "clientKey": {"$ref": "#/$defs/key"},
                "kind": {
                  "enum": [
                    "businessProcess", "businessActivity", "userRequirement",
                    "systemRequirement", "regulatoryRequirement",
                    "businessRule", "feature", "implementationRequirement"
                  ]
                },
                "operation": {"enum": ["create", "update", "link"]},
                "certainty": {"enum": ["confirmed", "derived", "toConfirm"]},
                "title": {"type": "string", "minLength": 1, "maxLength": 240},
                "description": {"type": "string", "maxLength": 12000},
                "parentKey": {
                  "anyOf": [{"$ref": "#/$defs/key"}, {"type": "null"}]
                },
                "targetId": {
                  "anyOf": [{"type": "string", "format": "uuid"}, {"type": "null"}]
                },
                "uiMode": {
                  "enum": ["withUi", "withoutUi", null]
                },
                "regulatorySource": {
                  "anyOf": [{"type": "string", "maxLength": 1000}, {"type": "null"}]
                },
                "sourceKeys": {"$ref": "#/$defs/keyList"},
                "activityKeys": {"$ref": "#/$defs/keyList"},
                "requirementKeys": {"$ref": "#/$defs/keyList"},
                "conditions": {
                  "type": "array", "maxItems": 100,
                  "items": {"$ref": "#/$defs/condition"}
                }
              }
            }
          }
        }
        """;

    /// <summary>Valid structural step 01 example with entirely fictional contents.</summary>
    public const string ExampleJson = """
        {
          "schema": "nodalis.sfd.import.v1",
          "step": "sfd-01",
          "runId": "exemple-fictif-01",
          "proposals": [
            {
              "clientKey": "pm-demo", "kind": "businessProcess",
              "operation": "create", "certainty": "toConfirm",
              "title": "Traiter des demandes fictives",
              "description": "Processus de démonstration, à confirmer.",
              "sourceKeys": [], "activityKeys": [],
              "requirementKeys": [], "conditions": []
            },
            {
              "clientKey": "act-demo", "kind": "businessActivity",
              "operation": "create", "certainty": "derived",
              "title": "Contrôler une demande fictive",
              "description": "Activité métier et non écran applicatif.",
              "parentKey": "pm-demo", "sourceKeys": [],
              "activityKeys": [], "requirementKeys": [], "conditions": []
            }
          ]
        }
        """;

    /// <summary>Complete, human-review-only output instructions for one step.</summary>
    /// <param name="step">sfd-01 through sfd-04.</param>
    /// <returns>Shared exact schema, example, formalism and review safeguards.</returns>
    public static string InstructionsForStep(string step)
    {
        if (step is not ("sfd-01" or "sfd-02" or "sfd-03" or "sfd-04"))
        {
            throw new ArgumentException("Étape SFD inconnue.", nameof(step));
        }

        return "## Contrat JSON obligatoire et complet pour Nodalis SFD\n" +
            "La réponse doit contenir un seul objet JSON brut sans Markdown ni prose.\n" +
            "schema = " + Schema + "; step = " + step + ".\n" +
            "runId et clientKey sont des références TEMPORAIRES du lot, " +
            "pas des GUID persistants : Nodalis résoudra les identités réelles.\n" +
            "Les mises à jour utilisent targetId UNIQUEMENT lorsqu'un GUID canonique " +
            "a été explicitement fourni parmi les sources ; ne l'invente jamais.\n" +
            "Les liens parentKey, activityKeys, requirementKeys et businessRuleKeys " +
            "désignent des clés du lot ou références canoniques connues à revoir.\n" +
            "Aucun champ chemin, commande, état de validation ou approbation automatique.\n" +
            "Les propositions incompletes restent des BROUILLONS, sans C/SC/RM fictives.\n" +
            "C EXU : Quand [condition], l'utilisateur doit pouvoir [action / résultat].\n" +
            "SC EXU : En tant que [RÔLE], je veux [ACTION] afin de " +
            "[BUT / BÉNÉFICE] sur [CANAL]. Si inconnu : [CANAL À CONFIRMER].\n" +
            "C EXS : Quand [condition], le système doit permettre de [action / résultat].\n" +
            "C EXR : Quand [condition], le système doit respecter [obligation applicable].\n" +
            "EXR sans source réglementaire prouvée : certainty = toConfirm.\n" +
            "SC EXS / EXR / EMO : précises et vérifiables, aucun patron imposé.\n" +
            "Une FCT possède uiMode = withUi OU withoutUi. Inclure les traitements " +
            "automatiques, extractions, fichiers et échanges sans IHM.\n" +
            "Les EMO sont un seul type implementationRequirement, pas trois " +
            "sous-types et pas un objet UML UseCase. Leur parent FCT est " +
            "une proposition métier, et l'emplacement physique reste à arbitrer.\n" +
            "Les RM sont proposées une seule fois sous businessRule et " +
            "référencées depuis les SC par businessRuleKeys.\n" +
            "Aucun import DOCX n'est possible. La réponse ne sera pas appliquée " +
            "sans examen et confirmation humaine.\n\n" +
            "### JSON Schema EXHAUSTIF\n" + JsonSchema + "\n\n" +
            "### Exemple de syntaxe (étape 01 uniquement, fictif)\n" +
            ExampleJson + "\n";
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Parses one proposal envelope STRICTLY, without creating or updating
    /// any SFD project objects. Unknown fields and schema versions fail closed.
    /// </summary>
    /// <param name="json">External untrusted response JSON.</param>
    /// <returns>Structural proposals only; each awaits human source review.</returns>
    public static SfdImportEnvelope Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > 512000)
        {
            throw new InvalidDataException("Réponse JSON SFD trop volumineuse.");
        }

        SfdImportEnvelope envelope;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json,
                new JsonDocumentOptions { MaxDepth = 32 });
            CheckDuplicateProperties(doc.RootElement);
            envelope = JsonSerializer.Deserialize<SfdImportEnvelope>(
                json, Options) ??
                throw new InvalidDataException("Réponse SFD vide.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "Réponse SFD hors contrat " + Schema + ".", exception);
        }

        Validate(envelope);
        return envelope;
    }

    /// <summary>Checks step roles, explicit evidence keys and stable client references.</summary>
    /// <param name="envelope">Parsed, untrusted external response.</param>
    public static void Validate(SfdImportEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Schema != Schema ||
            envelope.Step is not ("sfd-01" or "sfd-02" or "sfd-03" or "sfd-04") ||
            !IsKey(envelope.RunId) || envelope.Proposals is null ||
            envelope.Proposals.Count is < 1 or > 100)
        {
            throw new InvalidDataException("Enveloppe JSON SFD invalide ou non prise en charge.");
        }

        HashSet<string> identities = new(StringComparer.Ordinal);
        foreach (SfdImportProposal proposal in envelope.Proposals)
        {
            if (proposal is null || !IsKey(proposal.ClientKey) ||
                !identities.Add(proposal.ClientKey) ||
                proposal.Title is null || proposal.Title.Length is < 1 or > 240 ||
                proposal.Title.Any(char.IsControl) ||
                proposal.Description is null || proposal.Description.Length > 12000 ||
                proposal.Operation is not ("create" or "update" or "link") ||
                proposal.Certainty is not ("confirmed" or "derived" or "toConfirm") ||
                (proposal.ParentKey is not null && !IsKey(proposal.ParentKey)) ||
                (proposal.TargetId == Guid.Empty) ||
                ((proposal.Operation == "create") == proposal.TargetId.HasValue) ||
                proposal.SourceKeys is null || proposal.ActivityKeys is null ||
                proposal.RequirementKeys is null || proposal.Conditions is null ||
                proposal.Conditions.Count > 100 ||
                !ValidKeys(proposal.SourceKeys) ||
                !ValidKeys(proposal.ActivityKeys) ||
                !ValidKeys(proposal.RequirementKeys))
            {
                throw new InvalidDataException("Proposition SFD invalide ou dupliquée.");
            }

            string[] roles = envelope.Step switch
            {
                "sfd-01" => ["businessProcess", "businessActivity"],
                "sfd-02" => ["userRequirement", "systemRequirement",
                    "regulatoryRequirement", "businessRule"],
                "sfd-03" => ["feature"],
                _ => ["implementationRequirement"]
            };
            if (!roles.Contains(proposal.Kind, StringComparer.Ordinal))
            {
                throw new InvalidDataException("Type de proposition interdit pour cette étape.");
            }

            if ((proposal.Kind == "feature") != (proposal.UiMode is
                    ("withUi" or "withoutUi")) ||
                proposal.Kind != "feature" && proposal.UiMode is not null ||
                proposal.Kind != "feature" && proposal.ActivityKeys.Count > 0 ||
                proposal.Kind != "implementationRequirement" &&
                    proposal.RequirementKeys.Count > 0 ||
                proposal.Kind == "regulatoryRequirement" &&
                    proposal.Certainty == "confirmed" &&
                    string.IsNullOrWhiteSpace(proposal.RegulatorySource) ||
                proposal.Kind != "regulatoryRequirement" &&
                    !string.IsNullOrWhiteSpace(proposal.RegulatorySource))
            {
                throw new InvalidDataException(
                    "Catégorie IHM, traçabilité ou source réglementaire non conforme.");
            }

            bool canHaveConditions = proposal.Kind is "userRequirement" or
                "systemRequirement" or "regulatoryRequirement" or
                "implementationRequirement";
            if (!canHaveConditions && proposal.Conditions.Count > 0)
            {
                throw new InvalidDataException(
                    "Les blocs C/SC appartiennent aux exigences ou EMO uniquement.");
            }

            SfdDocumentKind? requirementKind = proposal.Kind switch
            {
                "userRequirement" => SfdDocumentKind.UserRequirement,
                "systemRequirement" => SfdDocumentKind.SystemRequirement,
                "regulatoryRequirement" => SfdDocumentKind.RegulatoryRequirement,
                _ => null
            };
            foreach (SfdImportCondition condition in proposal.Conditions)
            {
                if (condition is null || !IsKey(condition.ClientKey) ||
                    !identities.Add(condition.ClientKey) ||
                    condition.Subconditions is null ||
                    condition.Subconditions.Count > 100)
                {
                    throw new InvalidDataException("Condition SFD mal identifiée.");
                }

                SfdConditionBlockCodec.RequireStatement(condition.Statement);
                if (requirementKind.HasValue)
                {
                    SfdConditionBlockCodec.ValidateFormalism(
                        requirementKind.Value, condition.Statement, false);
                }

                foreach (SfdImportSubcondition subcondition in
                    condition.Subconditions)
                {
                    if (subcondition is null || !IsKey(subcondition.ClientKey) ||
                        !identities.Add(subcondition.ClientKey) ||
                        subcondition.BusinessRuleKeys is null ||
                        !ValidKeys(subcondition.BusinessRuleKeys))
                    {
                        throw new InvalidDataException("Sous-condition SFD invalide.");
                    }

                    SfdConditionBlockCodec.RequireStatement(subcondition.Statement);
                    if (requirementKind.HasValue)
                    {
                        SfdConditionBlockCodec.ValidateFormalism(
                            requirementKind.Value, subcondition.Statement, true);
                    }
                }
            }
        }
    }

    /// <summary>Recursively rejects duplicate JSON fields at every object level.</summary>
    /// <param name="element">Untrusted JSON node.</param>
    private static void CheckDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new InvalidDataException(
                        "Champs JSON dupliqués dans la réponse SFD.");
                }

                CheckDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                CheckDuplicateProperties(item);
            }
        }
    }

    /// <summary>Checks stable temporary references, never file paths or commands.</summary>
    /// <param name="key">One temporary or known reference.</param>
    /// <returns>Whether the value is a portable ASCII token.</returns>
    private static bool IsKey(string? key) =>
        !string.IsNullOrWhiteSpace(key) && key.Length <= 80 &&
        key.All(character => char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');

    /// <summary>Rejects missing, duplicated and unsafe list tokens.</summary>
    /// <param name="keys">Reference list.</param>
    /// <returns>Whether all keys are distinct and portable.</returns>
    private static bool ValidKeys(IReadOnlyList<string> keys) =>
        keys.Count <= 100 &&
        keys.All(IsKey) &&
        keys.Distinct(StringComparer.Ordinal).Count() == keys.Count;
}
