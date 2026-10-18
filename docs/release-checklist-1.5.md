# Qualification de sortie — Nodalis v1.5 (issue #134)

> **Statut : candidate develop, non validée pour publication.**
> Pas de merge vers `main`, tag ou release v1.5.0 avant vérification des gates.

## Automatisé en CI Windows

Le workflow [Build](../.github/workflows/build.yml) sur `develop` exécute :

- contrôle de contraintes locales, accessibilité et règles de style C# ;
- build Release de toute la solution et smoke tests sans dépendances externes ;
- packaging portable, reproductibilité et lancement du binaire contre le workspace RC ;
- publication d'un artefact de diagnostic CI (pas une release stable).

La suite `WorkspaceViewsV15ReleaseSmokeTests` ajoute : anciens manifests
conservés, persistance épinglée, corruption détectée, restauration du JSON de
définition, 12 000 items / 200 projets, pages déterministes, tâches terminées,
suppression d'une source, champs absents et 50 annulations répétées.

Ces tests ne valident pas à eux seuls les interactions WPF sur toutes les machines.

## Mesures et budgets

Lancer sous Windows, en configuration Release :

```powershell
dotnet run --project benchmarks/Nodalis.Benchmarks -c Release -- --documents 200
dotnet run --project benchmarks/Nodalis.Benchmarks -c Release -- --documents 2000
dotnet run --project benchmarks/Nodalis.Benchmarks -c Release -- --documents 12000
```

Observer les champs `views` : `projectionMilliseconds`, `firstPageMilliseconds`,
`secondPageMilliseconds`, `managedMemoryBytes`, `processWorkingSetBytes`.
Les budgets de départ (à calibrer sur un Windows 10/11 standard) sont :
moins de 150 ms pour un filtrage paginé de 12 000 items, moins de 250 Mo
d'augmentation mémoire à chaud, et aucune exception/corruption lors de
50 annulations. Ces budgets sont **proposés**, non certifiés par CI.

## Contrôles manuels bloquants

- [ ] Windows 10 et Windows 11 : lancer l'exécutable portable, ouvrir et fermer 20 fois les vues.
- [ ] DPI 100 %, 125 %, 150 % + dark mode : aucun bouton tronqué ni panneau masqué.
- [ ] Clavier seul : Ctrl+P, accès Vue sauvegardée, Tab, Entrée, navigation source.
- [ ] Épingler une vue : visible dans « Mes vues » et le dashboard après redémarrage.
- [ ] Projet renommé/supprimé : identité conservée ou diagnostic explicite, jamais réaffecté par homonyme.
- [ ] Éditer/terminer une tâche et enregistrer ; actualisation visible sans relancer.
- [ ] Modifier/supprimer/déplacer une fiche Markdown hors Nodalis : diagnostic/refresh correct.
- [ ] Annuler ou modifier les filtres rapidement : seuls les résultats récents restent visibles.
- [ ] Définition corrompue, backup/restauration, schéma workspace 0/1 et ancienne installation 1.0–1.4.
- [ ] Qualification Small/Medium/Large et budgets consignés avec matériel/OS.
- [ ] Aucune P0/P1 ouverte bloquante pour la publication.

## Restauration / rollback

1. Fermer Nodalis et copier le dossier workspace original en lieu sûr.
2. Restaurer le ZIP vérifié du workspace selon
   [sauvegardes](backups.md) et [migrations](workspace-migrations.md).
3. Restaurer, si nécessaire, `.nodalis-views.json` depuis la sauvegarde :
   seules les définitions sont restaurées, aucun résultat dérivé.
4. Les vues privées résident dans
   `%LOCALAPPDATA%/Nodalis/views/<workspace-guid>/views-private.json`
   et nécessitent une sauvegarde locale séparée.
5. En rollback vers v1.4, ignorer le JSON des vues : ne pas le supprimer,
   ne pas tenter de convertir ses résultats en notes.
6. Recharger le workspace, vérifier les GUID source et la lisibilité des notes,
   tâches, jalons, décisions et maintenances.

## Limite connue #133

Le cache en mémoire invalide la projection lors des changements source, mais
une invalidation peut encore lancer un scan complet du collecteur des tâches
et jalons. L'index des liens, lui, réutilise les empreintes existantes. La
granularité par source et la mesure réelle sous Windows restent indispensables
avant de clore #133 et #134.
