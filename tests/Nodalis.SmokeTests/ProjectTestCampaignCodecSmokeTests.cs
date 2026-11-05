using System.Security.Cryptography;
using System.Text;
using Nodalis.Core.Tests;

/// <summary>Pure frozen-passage and human result contract regressions.</summary>
internal static class ProjectTestCampaignCodecSmokeTests
{
    /// <summary>Verifies immutable historical snapshots and fail-closed parsing.</summary>
    public static void Run()
    {
        Guid testId = Guid.NewGuid();
        Guid campaignId = Guid.NewGuid();
        DateTimeOffset beginning = DateTimeOffset.UtcNow.AddHours(-2);
        ProjectTestFrozenDefinition sourceV1 =
            Freeze(testId, "## TEST-0001 — État initial\nRésultat fictif A\n");
        ProjectTestFrozenDefinition sourceV2 =
            Freeze(testId, "## TEST-0001 — État révisé\nRésultat fictif B\n");
        ProjectTestResultEvent firstResult = new()
        {
            Id = Guid.NewGuid(),
            TestId = testId,
            Outcome = ProjectTestOutcome.Failed,
            Actor = "Exécutant fictif",
            Rationale = "Résultat non conforme déclaré.",
            RecordedUtc = beginning.AddMinutes(15)
        };
        ProjectTestResultEvent secondResult = firstResult with
        {
            Id = Guid.NewGuid(),
            Outcome = ProjectTestOutcome.Passed,
            RecordedUtc = beginning.AddHours(1).AddMinutes(15),
            Rationale = "Nouveau passage après correction.",
            EvidenceNote = "preuve fictive non vérifiée"
        };
        ProjectTestCampaign campaign = new()
        {
            Id = campaignId,
            ProjectId = Guid.NewGuid(),
            Reference = "REC-0001",
            Title = "Campagne fictive",
            CreatedUtc = beginning.AddMinutes(-10),
            State = ProjectTestCampaignState.InProgress,
            Passages =
            [
                new ProjectTestPassage
                {
                    Id = Guid.NewGuid(),
                    Number = 1,
                    Environment = "Recette-A",
                    ApplicationVersion = "1.0",
                    StartedBy = "Responsable fictif",
                    StartedUtc = beginning,
                    Tests = [sourceV1],
                    Results = [firstResult]
                },
                new ProjectTestPassage
                {
                    Id = Guid.NewGuid(),
                    Number = 2,
                    Environment = "Recette-B",
                    ApplicationVersion = "1.1",
                    StartedBy = "Responsable fictif",
                    StartedUtc = beginning.AddHours(1),
                    Tests = [sourceV2],
                    Results = [secondResult]
                }
            ]
        };
        string markdown = ProjectTestCampaignMarkdownCodec.Format(campaign);
        ProjectTestCampaign parsed =
            ProjectTestCampaignMarkdownCodec.Parse(markdown);
        Assert(parsed.Passages.Count == 2 &&
               parsed.Id == campaign.Id &&
               parsed.Passages[0].Tests[0].Markdown == sourceV1.Markdown &&
               parsed.Passages[0].Results[0].Outcome ==
                   ProjectTestOutcome.Failed &&
               parsed.Passages[1].Tests[0].Markdown == sourceV2.Markdown &&
               parsed.Passages[1].Results[0].Outcome ==
                   ProjectTestOutcome.Passed &&
               parsed.Passages[1].ApplicationVersion == "1.1" &&
               ProjectTestCampaignMarkdownCodec.Format(parsed) == markdown,
            "Two passages must retain distinct frozen revisions and human results.");

        ProjectTestResultCorrectionEvent correction = new()
        {
            Id = Guid.NewGuid(),
            PassageId = campaign.Passages[0].Id,
            ResultId = firstResult.Id,
            TestId = testId,
            CorrectedOutcome = ProjectTestOutcome.Blocked,
            Actor = "Responsable fictif",
            Reason = "Erreur de saisie constatée, sans nouveau test.",
            RecordedUtc = beginning.AddHours(1).AddMinutes(30)
        };
        ProjectTestCampaign amended = campaign with
        {
            ResultCorrections = [correction]
        };
        string auditedMarkdown = ProjectTestCampaignMarkdownCodec.Format(amended);
        ProjectTestCampaign audited =
            ProjectTestCampaignMarkdownCodec.Parse(auditedMarkdown);
        IReadOnlyList<ProjectTestResultCorrectionRow> audit =
            ProjectTestResultCorrectionPolicy.ForPassage(
                audited, campaign.Passages[0].Id);
        Assert(audit.Count == 1 &&
               audit[0].OriginalOutcome == ProjectTestOutcome.Failed &&
               audit[0].CorrectedOutcome == ProjectTestOutcome.Blocked &&
               audited.Passages[0].Results[0].Outcome ==
                   ProjectTestOutcome.Failed &&
               audited.Passages[1].Results[0].Outcome ==
                   ProjectTestOutcome.Passed &&
               ProjectTestCampaignMarkdownCodec.Format(audited) ==
                   auditedMarkdown &&
               ProjectTestCampaignMarkdownCodec.Format(campaign) == markdown,
            "Correction is a separate durable audit event, not a rewritten " +
            "historical result or a new passage.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                [
                    correction,
                    correction with { Id = Guid.NewGuid() }
                ]
            }), "A result cannot receive two administrative corrections.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { PassageId = campaign.Passages[1].Id }]
            }), "A correction cannot target another frozen passage.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { TestId = Guid.NewGuid() }]
            }), "A correction cannot target another frozen test.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { CorrectedOutcome =
                        ProjectTestOutcome.Failed }]
            }), "A no-op correction is not an audit event.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { RecordedUtc =
                        firstResult.RecordedUtc.AddMinutes(-1) }]
            }), "Correction cannot predate its original human declaration.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { Actor = " " }]
            }), "A correction always has a human author.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { Reason = " " }]
            }), "A correction always has a human rationale.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            amended with
            {
                ResultCorrections =
                    [correction with { ResultId = Guid.NewGuid() }]
            }), "A correction cannot refer to an absent result.");

        string incident = "INC-" + Guid.NewGuid().ToString("N");
        string secondIncident = "INC-" + Guid.NewGuid().ToString("N");
        ProjectTestIncidentLinkEvent link = new()
        {
            Id = Guid.NewGuid(),
            PassageId = campaign.Passages[0].Id,
            ResultId = firstResult.Id,
            TestId = testId,
            IncidentId = incident,
            Kind = ProjectTestIncidentLinkKind.Linked,
            Actor = "Testeur fictif",
            Rationale = "Anomalie observée pendant la recette.",
            RecordedUtc = beginning.AddMinutes(35)
        };
        ProjectTestIncidentLinkEvent additional = link with
        {
            Id = Guid.NewGuid(),
            IncidentId = secondIncident,
            RecordedUtc = link.RecordedUtc.AddMinutes(1)
        };
        ProjectTestIncidentLinkEvent unlink = link with
        {
            Id = Guid.NewGuid(),
            Kind = ProjectTestIncidentLinkKind.Unlinked,
            RecordedUtc = link.RecordedUtc.AddMinutes(2),
            Rationale = "Mauvaise association corrigée explicitement."
        };
        ProjectTestCampaign linkedCampaign = campaign with
        {
            IncidentLinks = [link, additional, unlink]
        };
        string linkedMarkdown =
            ProjectTestCampaignMarkdownCodec.Format(linkedCampaign);
        ProjectTestCampaign parsedLinks =
            ProjectTestCampaignMarkdownCodec.Parse(linkedMarkdown);
        IReadOnlyList<ProjectTestLinkedIncident> linked =
            ProjectTestIncidentLinkPolicy.Active(parsedLinks, firstResult.Id);
        Assert(linked.Count == 1 &&
               linked[0].IncidentId == secondIncident &&
               parsedLinks.Passages[0].Results[0].Outcome ==
                   ProjectTestOutcome.Failed &&
               parsedLinks.Passages[1].Results[0].Outcome ==
                   ProjectTestOutcome.Passed &&
               ProjectTestCampaignMarkdownCodec.Format(parsedLinks) ==
                   linkedMarkdown &&
               ProjectTestCampaignMarkdownCodec.Format(campaign) == markdown,
            "Human links/unlinks are portable append-only audit without " +
            "changing frozen execution history.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link, link with
                {
                    Id = Guid.NewGuid(),
                    RecordedUtc = link.RecordedUtc.AddMinutes(1)
                }]
            }), "A second Linked event requires an intervening Unlinked.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [unlink]
            }), "Cannot unlink an incident absent from earlier audit.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with { ResultId = Guid.NewGuid() }]
            }), "An incident association needs a real human result.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with { PassageId =
                    campaign.Passages[1].Id }]
            }), "An incident association cannot move across passages.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with { IncidentId = "INC-not-an-id" }]
            }), "Maintenance references cannot be arbitrary strings.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with { Rationale = "trop court" }]
            }), "A human association requires a useful written rationale.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with { RecordedUtc =
                    firstResult.RecordedUtc.AddSeconds(-1) }]
            }), "No incident association may predate its human test result.");
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                IncidentLinks = [link with
                {
                    Id = correction.Id
                }],
                ResultCorrections = [correction]
            }), "An incident event cannot reuse a correction audit GUID.");

        Assert(!ProjectTestCampaignMarkdownCodec.IsReference("REC-0000") &&
               ProjectTestCampaignMarkdownCodec.IsReference("REC-0001") &&
               !ProjectTestCampaignMarkdownCodec.IsReference("REC-10000"),
            "Campaign references require canonical nonzero numbers.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                Passages =
                [
                    campaign.Passages[0] with
                    {
                        Tests = [sourceV1 with { Sha256 = new string('A', 64) }]
                    }
                ]
            }),
            "Altered captured text hashes must be rejected.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                Passages =
                [
                    campaign.Passages[0] with
                    {
                        Results = [firstResult, firstResult with
                        {
                            Id = Guid.NewGuid()
                        }]
                    }
                ]
            }),
            "A passage cannot quietly replace or duplicate a test outcome.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                Passages =
                [
                    campaign.Passages[0] with
                    {
                        Results = [firstResult with { TestId = Guid.NewGuid() }]
                    }
                ]
            }),
            "An outcome cannot target a test absent from the frozen passage.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            campaign with
            {
                Passages =
                [
                    campaign.Passages[0],
                    campaign.Passages[1] with { Number = 1 }
                ]
            }),
            "Passage numbers must strictly increase.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Parse(
            markdown.Replace("nodalis:test-campaign:v1",
                "nodalis:test-campaign:v2", StringComparison.Ordinal)),
            "Unknown metadata marker must fail closed.");

        Reject(() => ProjectTestCampaignMarkdownCodec.Parse(
            markdown.Replace("# REC-0001 —", "# REC-0002 —",
                StringComparison.Ordinal)),
            "Edited Markdown heading cannot claim a different campaign.");

        ProjectTestPassage unexecuted = campaign.Passages[1] with
        {
            Id = Guid.NewGuid(),
            Number = 3,
            StartedUtc = beginning.AddHours(2),
            Results = []
        };
        ProjectTestCampaign extended = campaign with
        {
            Passages = campaign.Passages.Append(unexecuted).ToArray()
        };
        IReadOnlyList<ProjectTestPassageResultRow> exact =
            ProjectTestCampaignProjection.GetPassageRows(
                extended, unexecuted.Id);
        IReadOnlyList<ProjectTestConsolidatedRow> consolidated =
            ProjectTestCampaignProjection.Consolidate(extended);
        Assert(exact.Count == 1 && exact[0].Outcome is null &&
               consolidated.Count == 2 &&
               consolidated.Single(item => item.Environment == "Recette-A")
                   .LatestOutcome == ProjectTestOutcome.Failed &&
               consolidated.Single(item => item.Environment == "Recette-B")
                   .LatestOutcome is null &&
               consolidated.Single(item => item.Environment == "Recette-B")
                   .PassageCount == 2 &&
               consolidated.Single(item => item.Environment == "Recette-B")
                   .RecordedResultCount == 1,
            "An unexecuted newer passage cannot inherit an older success.");
        Assert(ProjectTestCampaignProjection.GetPassageRows(
                   extended, campaign.Passages[1].Id)[0].Outcome ==
                   ProjectTestOutcome.Passed,
            "Historical exact passage result remains separately available.");

        // Repeated human passages share the same exact test body, but their
        // individual selections and passage IDs must still survive parsing.
        ProjectTestPassage[] repeated = Enumerable.Range(1, 80)
            .Select(number => campaign.Passages[0] with
            {
                Id = Guid.NewGuid(),
                Number = number,
                StartedUtc = beginning.AddMinutes(number),
                Results = []
            }).ToArray();
        ProjectTestCampaign large = campaign with { Passages = repeated };
        string largeMarkdown = ProjectTestCampaignMarkdownCodec.Format(large);
        ProjectTestCampaign restored =
            ProjectTestCampaignMarkdownCodec.Parse(largeMarkdown);
        Assert(restored.Passages.Count == 80 &&
               restored.Passages.All(item =>
                   item.Tests.Single().TestId == testId &&
                   item.Results.Count == 0) &&
               ProjectTestCampaignMarkdownCodec.Format(restored) ==
                   largeMarkdown,
            "Repeated frozen sections remain canonical without changing " +
            "the historical data format or inventing verdicts.");

        ProjectTestPassage forgedIdentity = repeated[5] with
        {
            Tests = [sourceV1 with { TestId = Guid.NewGuid() }]
        };
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            large with
            {
                Passages = repeated.Select((item, index) =>
                    index == 5 ? forgedIdentity : item).ToArray()
            }),
            "Caching a repeated SHA must not accept a forged block GUID.");

        ProjectTestPassage forgedContent = repeated[6] with
        {
            Tests = [sourceV1 with { Markdown = sourceV2.Markdown }]
        };
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            large with
            {
                Passages = repeated.Select((item, index) =>
                    index == 6 ? forgedContent : item).ToArray()
            }),
            "Caching a repeated SHA must still reject changed Markdown.");

        ProjectTestPassage unsafePath = repeated[7] with
        {
            Tests = [sourceV1 with { SourceRelativePath = "../Cahier.md" }]
        };
        Reject(() => ProjectTestCampaignMarkdownCodec.Format(
            large with
            {
                Passages = repeated.Select((item, index) =>
                    index == 7 ? unsafePath : item).ToArray()
            }),
            "Every repeated section must still validate its own source path.");

        Assert(campaign.Passages[0].Tests[0].Sha256 == sourceV1.Sha256 &&
               campaign.Passages[1].Tests[0].Sha256 == sourceV2.Sha256 &&
               firstResult.Outcome == ProjectTestOutcome.Failed,
            "Read/format must not mutate previous passage snapshots.");
    }

    /// <summary>Makes a synthetic test definition snapshot.</summary>
    /// <param name="id">Source test ID.</param>
    /// <param name="body">Exact frozen Markdown.</param>
    /// <returns>Valid frozen test definition.</returns>
    private static ProjectTestFrozenDefinition Freeze(Guid id, string body)
    {
        int firstNewline = body.IndexOf('\n');
        string frozen = body.Insert(firstNewline + 1,
            Nodalis.Core.Markdown.MarkdownBlockIdentityCodec.FormatMarker(id) +
            "\n");
        return new ProjectTestFrozenDefinition
        {
            TestId = id,
            Reference = "TEST-0001",
            SourceRelativePath =
                "Applications/Fictive/Projets/Projet-A/Tests/Cahier.md",
            Markdown = frozen,
            Sha256 = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(frozen))),
            HeadingLine = 2
        };
    }

    /// <summary>Expect rejection of a noncanonical test campaign.</summary>
    /// <param name="action">Pure invalid candidate.</param>
    /// <param name="message">Safe synthetic diagnostic.</param>
    private static void Reject(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>Checks an invariant.</summary>
    /// <param name="condition">Expected invariant.</param>
    /// <param name="message">Safe failure message.</param>
    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
