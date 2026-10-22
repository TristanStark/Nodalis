# Nodalis 1.9 — Gate de sortie

Statut : **prépublication, develop**. Aucune clôture automatique des issues P0 ni merge vers main tant que les scénarios manuels bloquants restent ouverts.

## Contrôles automatisés obligatoires

- [ ] `dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check` — absence de `var`, commentaires XML
- [ ] `dotnet build Nodalis.sln --configuration Release`
- [ ] `dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release`
- [ ] Fixture Word 105 sauts de page : styles Heading, sections normalisées, preview multi-destinations, chemins exacts du plan et sauvegarde source/provenance
- [ ] Collisions interprocessus après approbation : fichier Markdown et copie DOCX — refus sans renommage
- [ ] Vérification créateur/profil : projet Ultra-simple et Moyen, fichiers racines singleton Jalons/Glossaire
- [ ] CI Build sur develop : tests de non-régression, vérification binaire portable Win10/11 et artefact disponible
- [ ] Vérifier SHA du commit avant qualification : les résultats d'une CI antérieure ne qualifient pas le nouveau head

## Qualification Windows 10/11 à compléter par validation réelle

- [ ] Import structuré Développement/Tests/Recette, exclusion d'un sous-arbre et d'un bloc individuel
- [ ] Mapping, glisser-déposer/clavier, aperçu comparé Word/Markdown, dry-run cohérent
- [ ] 100+ pages sur un vrai DOCX Office : performances / DPI 100, 125 et 150 %, thèmes et keyboard focus
- [ ] Tableaux fusionnés, images intégrées et liens externes non téléchargés
- [ ] Réimport à trois voies (Word seul, Nodalis seul, les deux, déplacement, renommage, suppression), aucune perte de notes
- [x] Journal d'anticipation du premier import : sauvegarde binaire manifeste, checksums des créations, reprise ou rollback simulés (#165)
- [x] Copie des images Word via fichier temporaire et renommage atomique, suppression sûre des sorties modifiées par un autre processus
- [ ] **Arrêt forcé réel sur Windows** du premier import aux checkpoints projet / image / Markdown / provenance, avec validation des temporaires et du résultat (#165, #171)
- [ ] Annulation, modification externe, permissions insuffisantes, paquet ZIP/XML corrompu
- [ ] Retour utilisateur via `docs/docx-1.9-manual-qualification.md` ; aucun P0/P1 bloquant

## Critères pour publication

La 1.9 est publiable **uniquement** si le gate #171 est coché, les P0 sont clos après qualification, les P1 retenus ou explicitement reportés, puis merge vers main, tag `v1.9.0` et release avec notes complètes.

XLSX et VSDX sont **hors 1.9** : ne pas les annoncer comme des formats importables.
