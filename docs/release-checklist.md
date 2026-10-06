# Nodalis 1.0 — checklist Release Candidate

Ce document est le gate de passage de `develop` vers le tag stable `v1.0.0`.
Le tag stable ne doit pas être créé tant que les validations automatiques et
manuelles ci-dessous ne sont pas terminées.

## Gate automatisé

La CI doit valider les points suivants sur le commit candidat :

- [x] migrations de workspace couvertes par la suite de régression ;
- [x] sauvegarde et restauration couvertes ;
- [x] update, rollback et récupération après interruption couverts ;
- [x] contraintes zéro dépendance tierce et zéro réseau applicatif contrôlées ;
- [x] audit WPF dark mode / accessibilité exécuté ;
- [x] fixture Large et budgets de performance couverts ;
- [x] documentation 1.0 versionnée et embarquée dans l'archive ;
- [x] package Windows x64 self-contained généré ;
- [x] SHA256SUMS.txt vérifié contre le ZIP ;
- [x] second build du même commit comparé byte-for-byte au premier ;
- [x] binaire publié démarré contre le workspace `tests/ReleaseCandidateWorkspace` ;
- [x] navigation, recherche et résolution d'un lien interne vérifiées par ce binaire ;
- [x] aucune issue ouverte portant le label `blocker` au lancement du workflow de release.

Le workflow `Build` applique le gate technique à chaque push. Le workflow
`Portable Windows Release` réexécute les mêmes contrôles avant de produire un
artefact ou une GitHub Release.

## Workspace RC

`tests/ReleaseCandidateWorkspace` est un workspace non confidentiel et
versionné. Il contient au minimum un workspace schéma 1, une application, un
projet, des notes rapides, glossaires, jalons, sections Technique et Tests, une
réunion, une décision, une tâche Markdown et un lien interne vers Architecture.md.

Le texte `release-candidate-sentinel` sert de sentinelle de recherche.

Le mode `--smoke-test-workspace <path>` du binaire publié charge ce workspace,
initialise les templates, construit la navigation, exécute une recherche exacte
et rafraîchit l'index de liens. Le workflow travaille toujours sur une copie
temporaire de la fixture.

## Création d'une RC

Utiliser manuellement le workflow `Portable Windows Release` avec une version
de prérelease, par exemple :

    1.0.0-rc.1

Un lancement manuel produit un artefact mais ne crée pas de GitHub Release.
Télécharger cet artefact pour les essais Windows réels.

## Validation Windows manuelle obligatoire

GitHub Actions `windows-latest` valide le binaire Windows dans un environnement
Windows Server hébergé. Cela ne remplace pas l'essai demandé sur les systèmes
clients réellement supportés.

Avant `v1.0.0`, cocher manuellement :

- [ ] archive RC extraite et `Nodalis.exe` lancé sur Windows 10 x64 ;
- [ ] workspace de test ouvert et modifié sur Windows 10 x64 ;
- [ ] update puis rollback de la RC exercés sur Windows 10 x64 ;
- [ ] archive RC extraite et `Nodalis.exe` lancé sur Windows 11 x64 ;
- [ ] workspace de test ouvert et modifié sur Windows 11 x64 ;
- [ ] update puis rollback de la RC exercés sur Windows 11 x64 ;
- [ ] aucun blocker fonctionnel découvert pendant ces essais.

### Smoke UX issu de la RC terrain (#90)

Sur au moins une session Windows client, vérifier également :

- [ ] navigation gauche sans barre horizontale parasite ni contenu rogné ;
- [ ] un ancien onglet `Jalons/Jalons.md` est réparé vers `Jalons.md` ;
- [ ] survol d'un terme surligné du glossaire : définition et scope visibles ;
- [ ] barre principale regroupée par thèmes et entièrement accessible à 1024 px ;
- [ ] `Nouvelle tâche` trouvable via Ctrl+P et création effective dans le projet courant ;
- [ ] filtres ComboBox des vues Tâches et Kanban entièrement sombres ;
- [ ] aperçu Mermaid/Flowchart vérifié avec définitions de nœuds étendues et chaînes de liens.

Conserver le numéro de version RC, le commit et les résultats de ces essais dans
l'issue GitHub #79.

### Smoke UX issu de la RC terrain (#91)

Sur le même poste Windows client, vérifier également :

- [ ] création d'une tâche avec échéance via le DatePicker, puis réouverture avec la même date ;
- [ ] priorité et statut lisibles dans le champ fermé, le dropdown, le survol et la sélection ;
- [ ] sélection application/projet/dossier/section/document entièrement sombre, même quand l'arbre perd le focus ;
- [ ] filtres Calendrier et Kanban limités aux libellés utilisateur, sans `FilterOption {...}` ni `ScopeFilterOption {...}` ;
- [ ] tâche datée visible dans le calendrier au bon jour et ouvrable par double-clic ;
- [ ] compteur du calendrier cohérent avec les éléments réellement visibles après filtres et changement de mois ;
- [ ] bandeaux supérieurs lisibles sans crop ni chevauchement à 1024 px et avec mise à l'échelle Windows.

## Passage en stable

Lorsque le gate automatisé est vert et la matrice Windows manuelle entièrement
cochée :

1. vérifier une dernière fois qu'aucune issue `blocker` n'est ouverte ;
2. vérifier que le commit candidat est exactement celui testé en RC ;
3. créer le tag `v1.0.0` sur ce commit ;
4. laisser le workflow de release reconstruire et revérifier le package ;
5. vérifier les artefacts `Nodalis-win-x64.zip` et `SHA256SUMS.txt` de la
   GitHub Release avant annonce de la stable.
