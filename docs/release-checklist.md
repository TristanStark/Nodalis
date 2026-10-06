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

Conserver le numéro de version RC, le commit et les résultats de ces essais dans
l'issue GitHub #79.

## Passage en stable

Lorsque le gate automatisé est vert et la matrice Windows manuelle entièrement
cochée :

1. vérifier une dernière fois qu'aucune issue `blocker` n'est ouverte ;
2. vérifier que le commit candidat est exactement celui testé en RC ;
3. créer le tag `v1.0.0` sur ce commit ;
4. laisser le workflow de release reconstruire et revérifier le package ;
5. vérifier les artefacts `Nodalis-win-x64.zip` et `SHA256SUMS.txt` de la
   GitHub Release avant annonce de la stable.
