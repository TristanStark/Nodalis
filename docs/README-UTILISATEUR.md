# Nodalis 1.0 — guide utilisateur

Nodalis est une application Windows locale de prise de notes et d'aide à la
gestion de projet. Les données restent dans un workspace normal composé
principalement de Markdown et de JSON.

## 1. Installer et démarrer

1. Récupérez l'archive Nodalis-win-x64.zip.
2. Extrayez tout son contenu dans un dossier local, par exemple C:\Outils\Nodalis.
3. Lancez Nodalis.exe.
4. Au premier démarrage, choisissez le dossier qui contiendra le workspace.

La distribution Windows x64 est self-contained : aucun runtime .NET, Visual
Studio ou installateur supplémentaire n'est nécessaire.

Le dossier de l'application et le workspace doivent rester distincts.

## 2. Où sont stockées les données

Le workspace contient les données métier :

- fichiers Markdown ;
- manifests JSON ;
- pièces jointes ;
- sources importées ;
- corbeille ;
- templates ;
- index techniques nécessaires à l'identité des documents.

Les préférences locales sont stockées dans :

    %LOCALAPPDATA%\Nodalis\preferences.json

Elles ne font pas partie du workspace et leur perte ne détruit pas les données
métier.

Le détail du format se trouve dans workspace-format.md et data-contracts-v1.md.

## 3. Organisation du workspace

Nodalis utilise un workspace unique afin de voir tous les travaux sans changer
de base de données ou relancer l'application.

La structure métier principale est :

- Applications ;
- Modules, facultatifs, à l'intérieur d'une application ;
- Projets ;
- Sous-projets, qui restent des projets complets.

Un projet possède au minimum les rôles Jalons, Technique, Glossaire et Tests.
Jalons et Glossaire sont stockés sous forme de fichiers à la racine du projet.
Les autres sections peuvent être des dossiers et la structure reste
personnalisable.

Utilisez Structure du projet pour ajouter, renommer, réordonner ou déplacer les
sections et documents sans éditer manuellement le manifest du projet.

## 4. Éditeur Markdown

L'éditeur travaille directement sur les fichiers Markdown du workspace.

Fonctions principales :

- aperçu Markdown ;
- autosave sûr ;
- détection des modifications externes ;
- gras, italique et code inline ;
- propriétés de document via front matter ;
- liens internes ;
- flowcharts Markdown rendus localement.

Un document sans front matter reste parfaitement valide.

Pour les flowcharts, consultez markdown-flowcharts.md.

## 5. Liens, backlinks et relations

La syntaxe de lien interne est lisible dans le Markdown :

    [[Nom du document]]

Nodalis maintient un registre d'identité afin qu'un document renommé ou déplacé
conserve son identité. L'ancien nom peut rester un alias.

Les backlinks permettent de voir les documents qui référencent la cible.

Les relations typées ajoutent un lien métier explicite, par exemple dépend de,
remplace, implémente, teste, documente ou bloque. Leur identité cible repose sur
un GUID stable et non sur le texte affiché.

Ne supprimez pas manuellement .nodalis-links.json comme s'il s'agissait d'un
cache ordinaire : la collection des cibles participe à l'identité technique des
documents du schéma 1.

## 6. Notes rapides

Les notes rapides existent aux scopes :

- Global ;
- Application ;
- Projet.

Les modules structurent une application mais ne créent pas un quatrième scope
de connaissance.

Ctrl+Alt+N capture rapidement une note. Ctrl+Shift+Q ouvre la vue agrégée.

## 7. Recherche

La recherche est fuzzy par défaut et accepte les fautes courantes et les mots
incomplets. Le mode exact conserve une recherche littérale insensible à la
casse.

Les résultats sont classés Projet, puis Application, puis Global lorsque la
qualité est équivalente.

Filtres utiles :

- type:specification ;
- date:2026-10-05 ;
- project:"Projet Patate" ;
- status:active ;
- owner:alice ;
- tag:api ;
- @reviewer:alice pour une propriété personnalisée.

Les valeurs contenant des espaces peuvent être placées entre guillemets.

## 8. Tâches, Kanban, jalons et calendrier

Les tâches restent des checkboxes Markdown. La vue consolidée et le Kanban
proposent une projection et ne créent pas de seconde source de vérité.

Exemple :

    - [ ] Préparer la recette | Responsable: Alice | Échéance: 2026-10-10 | Priorité: Haute | Statut: En cours | Tags: api, recette

Les jalons restent dans Jalons.md. Ils peuvent inclure une date cible, un
statut, une description, un lien et des dépendances. Les dépendances incohérentes
ou cycliques sont signalées plutôt que corrigées silencieusement.

Le calendrier agrège les éléments datés du workspace.

## 9. Réunions et décisions

L'assistant de réunion crée des comptes-rendus Markdown et peut extraire les
décisions proposées dans le contenu.

Une décision validée devient un Decision Record Markdown lié à sa source.
Les décisions peuvent être recherchées et conserver un cycle de vie, notamment
Active, Deprecated ou Superseded.

Les liens entre une décision et sa source restent navigables après renommage.

## 10. Importer et exporter

L'import DOCX ne modifie jamais le fichier Word original. Nodalis travaille sur
une copie locale et affiche une analyse puis un plan d'écriture avant validation.

L'import Markdown en masse permet de prévisualiser les documents, découvrir les
pièces jointes relatives et éviter les doublons.

L'export de projet peut produire des sorties portables Markdown, HTML hors ligne
ou DOCX sans modifier le projet source.

Les détails et limites sont documentés dans import-export.md.

## 11. Corbeille

Une suppression standard déplace l'élément dans Corbeille au lieu de le détruire
immédiatement.

La restauration refuse d'écraser une cible déjà existante. Le vidage de la
corbeille est l'action définitive.

Applications, modules, projets et documents conservent leurs données d'origine
dans l'entrée de corbeille.

## 12. Sauvegardes et restauration

Le gestionnaire de sauvegardes crée des ZIP standards hors du workspace.
Les sauvegardes peuvent être manuelles ou automatiques avec rétention.

Une restauration vise un dossier distinct et vide. Nodalis valide l'archive,
extrait dans un staging privé puis valide le workspace restauré avant de le
rendre disponible.

Le guide détaillé se trouve dans backups.md.

## 13. Mise à jour et rollback

Nodalis installe une release depuis une archive locale.

Avant fermeture, l'application vérifie :

- produit et version ;
- cible Windows x64 ;
- compatibilité du workspace ;
- présence des fichiers ;
- tailles ;
- SHA-256.

Nodalis.Updater.exe effectue ensuite la bascule et vérifie que la nouvelle
version démarre. La version précédente reste disponible dans un dossier
.previous.

En cas d'échec de démarrage ou d'interruption, le mécanisme transactionnel tente
de restaurer la dernière version connue bonne.

Voir update-recovery.md pour le détail.

## 14. Compatibilité des workspaces

Le schéma 1 est le format Nodalis 1.0.

Un ancien workspace connu peut nécessiter une migration avant écriture.
Un workspace plus récent que la version installée est ouvert uniquement en
lecture de secours Markdown afin d'éviter toute corruption.

Voir workspace-migrations.md et workspace-compatibility.md.

## 15. Raccourcis principaux

| Raccourci | Action |
| --- | --- |
| Ctrl+N | Nouvelle note |
| Ctrl+Shift+N | Nouveau projet / sous-projet |
| Ctrl+Alt+N | Capturer une note rapide |
| Ctrl+Shift+Q | Voir les notes rapides agrégées |
| Ctrl+F | Recherche |
| Ctrl+K | Insérer ou ouvrir un lien interne |
| Ctrl+P | Command Palette |
| Ctrl+S | Forcer l'enregistrement |
| Ctrl+B | Gras |
| Ctrl+I | Italique |
| Ctrl+` | Code inline |

Les contrôles doivent rester accessibles au clavier. L'audit détaillé se trouve
dans accessibility.md.

## 16. Confidentialité et réseau

Nodalis n'effectue aucun appel réseau applicatif. Les données restent locales à
la machine et aux emplacements choisis.

Un dossier du workspace peut être synchronisé par un outil externe, mais Nodalis
ne dépend d'aucun service de synchronisation.

## 17. Windows SmartScreen

Un exécutable non signé peut déclencher SmartScreen au premier lancement.
Vérifiez l'origine de l'archive et, si disponible, comparez son SHA-256 avec
SHA256SUMS.txt.

## 18. Dépannage

Pour les erreurs d'ouverture, les conflits de fichiers, les imports, les
sauvegardes, les updates, les liens ou les crashs, consultez
troubleshooting.md.

Pour un crash ou un problème de récupération de session, consultez également
crash-diagnostics.md.

## 19. Documentation complémentaire

Le sommaire complet des documents 1.0 est docs/README.md dans le dépôt et Documentation/README.md dans l'archive portable.
