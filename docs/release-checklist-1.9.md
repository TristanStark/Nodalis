# Nodalis 1.9 — Gate de sortie

Statut : **qualification utilisateur acceptée le 31 octobre 2026 ; publication v1.9.0 autorisée**.
Le passage sur `main`, le tag, l'archive et la release GitHub sont exécutés par la chaîne de publication. Les scénarios manuels ont été acceptés explicitement par l'utilisateur ; ils ne sont pas assimilés à des mesures d'une CI partagée.

**Preuve de contrôle avant release :** workflow Build sur `develop` [41190039786](https://github.com/TristanStark/Nodalis/actions/runs/41190039786) terminé avec succès le 31 octobre 2026 ; la CI de publication reconstruit et revalide les binaires à partir du commit de merge `main`.

## Contrôles automatisés obligatoires

- [x] `dotnet run --project tools/Nodalis.CodeStyle/Nodalis.CodeStyle.csproj --configuration Release -- --check` — absence de `var`, commentaires XML
- [x] `dotnet build Nodalis.sln --configuration Release`
- [x] `dotnet run --project tests/Nodalis.SmokeTests/Nodalis.SmokeTests.csproj --configuration Release`
- [x] Fixture Word 105 sauts de page : styles Heading, sections normalisées, preview multi-destinations, chemins exacts du plan et sauvegarde source/provenance
- [x] Collisions interprocessus après approbation : fichier Markdown et copie DOCX — refus sans renommage
- [x] Vérification créateur/profil : projet Ultra-simple et Moyen, fichiers racines singleton Jalons/Glossaire
- [x] Jalons : coller dans `Jalons.md` des dates `JJ/MM/AAAA`, sans `Id` (entête court ou sept colonnes), puis modifier/supprimer un jalon et le déplacer dans le calendrier ; vérifier l'ID stable, l'affichage et la compatibilité `AAAA-MM-JJ`.
- [x] CI Build sur develop : tests de non-régression, vérification binaire portable Win10/11 et artefact disponible
- [x] Vérifier SHA du commit avant qualification : les résultats d'une CI antérieure ne qualifient pas le nouveau head

## Qualification Windows 10/11 validée par l'utilisateur

- [x] Import structuré Développement/Tests/Recette, exclusion d'un sous-arbre et d'un bloc individuel
- [x] Mapping, glisser-déposer/clavier, aperçu comparé Word/Markdown, dry-run cohérent
- [x] 100+ pages sur un vrai DOCX Office : performances / DPI 100, 125 et 150 %, thèmes et keyboard focus
- [x] Tableaux fusionnés, images intégrées et liens externes non téléchargés
- [x] Réimport à trois voies (Word seul, Nodalis seul, les deux, déplacement, renommage, suppression), aucune perte de notes
- [x] Journal d'anticipation du premier import : sauvegarde binaire manifeste, checksums des créations, reprise ou rollback simulés (#165)
- [x] Copie des images Word via fichier temporaire et renommage atomique, suppression sûre des sorties modifiées par un autre processus
- [x] Test Windows avec **processus enfant réellement terminé** juste après la première écriture Markdown : récupération du manifeste et conservation de la source/notes
- [x] Arrêt forcé réel dans la CI Windows après création de projet, copie d'image, Markdown, source DOCX et provenance, puis récupération au redémarrage
- [x] Inspection manuelle des temporaires après crash et vérification de l'UI de récupération (#165, #171)
- [x] Annulation, modification externe, permissions insuffisantes, paquet ZIP/XML corrompu
- [x] Retour utilisateur via `docs/docx-1.9-manual-qualification.md` ; aucun P0/P1 bloquant
- [x] Performance #189 : Word 100+ pages, sélectionner/désélectionner rapidement une sous-section, changer de titre puis annuler ; vérifier que le contenu final, le dry-run, les exclusions et le réimport restent identiques
- [x] Performance #184/#186 : Markdown 100k+ caractères, 30 onglets, frappe continue/bascule d'onglets/fermeture pendant parsing ; aucun aperçu périmé ni erreur WPF inter-thread
- [x] Performance #181/#183/#188 : diagnostics p95/p99, alternance autosave/recherche/vues/navigation, changements externes et génération d'index : aucun catalogue JSON obsolète ni blocage durable


- [x] P0 performance onglets (#184/#185/#190) : ouvrir 30 puis 100 documents, fermer ceux à droite et fermer les autres ; mesurer la durée et le retard Dispatcher, conserver sélection/split, sans gel de plusieurs secondes
- [x] P0 sûreté fermeture : plusieurs onglets modifiés, autosave et conflit externe ; vérifier conservation du fichier, refus d'abandon sans confirmation, aucune perte de texte
- [x] Retour de veille : laisser plusieurs onglets avec autosave en attente, suspendre/reprendre Windows ; vérifier absence de tempête d'index et fonctionnement du preview/backup, observer mémoire/GC sans conclure sur une seule capture
- [x] Binding worktree : vérifier absence des erreurs de liaison WPF FindAncestor/VerticalContentAlignment en créant/recyclant des TreeViewItem sous Visual Studio

## Critères pour publication — acceptés

La 1.9 est publiable **uniquement** si le gate #171 est coché, les P0 sont clos après qualification, les P1 retenus ou explicitement reportés, puis merge vers main, tag `v1.9.0` et release avec notes complètes.

XLSX et VSDX sont **hors 1.9** : ne pas les annoncer comme des formats importables.
