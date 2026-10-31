# DOCX v1.9 — Traçabilité des imports (#163)

## Où retrouver la provenance ?

Après validation du plan d'import avancé DOCX, Nodalis conserve :

- la copie binaire du Word d'origine sous `Imports/Sources/` ;
- le ou les fichiers Markdown générés dans les sections du projet ;
- un manifeste versionné `.nodalis-imports/<identifiant-import>.json` **dans le projet cible**.

Ce manifeste est un fichier JSON ordinaire, indépendant de Word, de l'IA et
de toute connexion réseau. Il est enregistré après les fichiers Markdown et
la copie du document. Son chemin fait partie du plan d'import affiché avant
confirmation.

## Contenu du manifeste

| Donnée | Rôle |
| --- | --- |
| `schemaVersion`, `importId`, `importedAtUtc` | Traçabilité et version du format |
| `projectId` | Identité Nodalis du projet destinataire |
| `sourceFileName`, `sourceSha256` | Nom lisible et empreinte **du contenu** du DOCX |
| `sourceCopyRelativePath` | Copie locale portable, sans chemin absolu d'origine |
| `previousImportId`, `reimportOperationId` | Chaînage vers le précédent import et identité de transaction ; facultatifs pour les imports originaux |
| `fragments[].reimportResolution` | Action qui a produit le snapshot actuel (`Word`, `Manual`, `Nodalis`) |
| `fragments[].latestWordMarkdownSnapshot`, `latestWordSnapshotSha256` | Texte constaté dans le nouveau Word et empreinte de contrôle, même quand Nodalis est conservé |
| `approvedPlanFingerprint` | Empreinte du plan explicitement approuvé, si fournie |
| `appliedMappingProfile` | Snapshot du profil réellement appliqué, y compris les règles et overrides, si présent |
| `excludedSectionIndexes` | Sections exclues volontairement |
| `fragments[]` | Une entrée par section Word retenue, même après fusion vers un même fichier |
| `fragments[].sourceSectionIndex`, `headingBlockIndex` | Position structurelle d'origine |
| `fragments[].fragmentId` | Identité unique pour distinguer deux titres identiques |
| `fragments[].selectedBlockIndexes` | Sélection exacte de blocs |
| `fragments[].sourceHeading`, `sourceParentPath`, `sourceWordStyleId`, `sourceHeadingLevel` | Titre Word et contexte hiérarchique/style pour distinguer les sections homonymes |\n| `fragments[].targetSection`, `targetSectionId` | Section Nodalis et son GUID canonique |
| `fragments[].destinationRelativePath` | Fichier Markdown produit |
| `fragments[].baseMarkdown`, `fragmentSha256` | Snapshot et empreinte du fragment importé |
| `fragments[].destinationSha256` | Empreinte de tout le Markdown de destination au commit |

Les snapshots servent de **base de comparaison** pour le futur réimport à
trois voies (#164) : Word initial / nouvelle version Word / modifications
locales Nodalis. Ils dupliquent donc volontairement une partie du contenu
du Word dans le workspace local. Si le document contient des données
confidentielles, la sauvegarde du workspace doit être protégée comme le
document lui-même.

## Sources identiques, renommées et homonymes

L'empreinte SHA-256 distingue deux DOCX de même nom mais de contenus
différents. Le nom reste un indice lisible, jamais une preuve suffisante pour
écraser un fichier. La recherche de manifestes peut remonter plusieurs
candidats ; l'issue #164 ajoutera l'arbitrage explicite, notamment pour les
titres homonymes, sections déplacées ou renommées et modifications concurrentes.

Le manifeste est **immuable** pour cet import. Les imports suivants
obtiennent leur propre identité et un nouveau fichier JSON.

Pour les réimports qualifiés, `DocxReimportProvenanceBuilder` produit un nouveau
manifeste lié au précédent, avec une **nouvelle copie Word propre à l'opération**
(`Imports/Sources/reimport-<GUID>.docx`). Les fragments gardent un GUID
stable d'une génération à la suivante quand leur identité a été confirmée ;
les sections supprimées du Word restent consultables dans l'historique parent.
Une fusion manuelle utilise comme nouvelle base le contenu effectivement
accepté, tout en conservant le texte Word observé séparément. Une décision
« conserver Nodalis » ne transforme pas le Word rejeté en faux contenu appliqué.
Le réimport n'est pas encore activé dans l'interface de validation.

## Sécurité et compatibilité

- Les anciens projets sans `.nodalis-imports/` continuent de fonctionner
  sans migration ni fichier supplémentaire lors d'une simple ouverture.
- Les chemins enregistrés sont relatifs au workspace ; la lecture rejette
  les chemins sortant de celui-ci.
- Les snapshots sont contrôlés par SHA-256 à la lecture.
- Les schémas non reconnus et les JSON corrompus sont refusés explicitement.
- Chaque manifeste est sérialisé dans un fichier temporaire dans son
  répertoire, synchronisé, puis installé par **renommage atomique sans
  écrasement**. Même deux créations concurrentes avec le même `importId`
  ne peuvent pas remplacer une version existante. Les fichiers temporaires
  sont nettoyés en cas d'échec.
- En cas d'échec de l'import, Nodalis tente le rollback des nouvelles
  écritures, y compris le manifeste et la copie DOCX importée.
- Aucun manifest historique n'est écrasé par un nouvel import.

**Périmètre :** les imports DOCX initiaux enregistrent la provenance ;
le backend de #164 prépare aussi les générations de réimport, avec sauvegarde
transactionnelle du Word, des Markdown et de leurs métadonnées. L'interface
affiche le comparateur et les arbitrages en lecture seule. L'application du
réimport reste désactivée jusqu'à la qualification de bout en bout.
