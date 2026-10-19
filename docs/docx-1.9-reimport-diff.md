# DOCX v1.9 — Réimport différentiel : socle de comparaison (#164)

## État du développement

Le premier incrément de #164 ajoute `DocxReimportDiffEngine`, un moteur
**purement consultatif et sans écritures**. Il s'appuie sur le manifeste
de provenance de l'import précédent (#163). La sélection du manifeste et la
correspondance entre anciennes et nouvelles sections Word devront être
confirmées explicitement dans l'assistant de réimport.

## Matrice de classification

| Word récent comparé au snapshot initial | Markdown Nodalis comparé à la destination initiale | Résultat |
| --- | --- | --- |
| Inchangé | Inchangé | `Unchanged` |
| Modifié | Inchangé | `WordChanged` |
| Inchangé | Modifié | `NodalisChanged` |
| Modifié | Modifié | `BothChanged` — conflit |
| Section supprimée | Toute situation | `WordDeleted` — conserver Nodalis |
| Destination manquante | Toute situation | `DestinationMissing` — pas de recréation automatique |
| Section déplacée ou renommée, texte inchangé | Inchangé | `SourceMovedOrRenamed` — confirmer l'identité |

Si une section a été renommée **et** modifiée, l'état reste
`WordChanged` ou `BothChanged` mais
`RequiresExplicitResolution` est actif.

Pour détecter les changements locaux, la comparaison utilise l'empreinte
SHA-256 du **fichier Markdown entier** au moment du dernier import, et non
uniquement celle du fragment concerné. Quand plusieurs sections Word
partagent un fichier cible, une modification locale de n'importe laquelle
d'entre elles bloque ainsi toute interprétation trompeuse de « fichier
inchangé ». Cette approche est volontairement conservatrice.


## Appariement entre versions Word

Le moteur `DocxReimportSectionMatcher` compare toutes les sections de l'ancien
manifeste aux sections nouvellement analysées. L'ordre de priorité est :

1. contenu Markdown du corps inchangé et **unique** sur les deux versions ;
2. titre et chemin hiérarchique de ses parents inchangés et **uniques** ;
3. titre globalement unique (mode compatible avec les anciens manifestes sans contexte).

Un titre identique ou une position semblable ne sont **jamais** suffisants
pour arbitrer deux sections ambiguës. Les positions constituent uniquement
des candidats à l'examen manuel. Les sections associées, nouvelles, absentes
et ambiguës sont conservées séparément dans le rapport.

Les nouveaux imports retiennent désormais le chemin des titres parents, leur
niveau et le style Word lorsqu'ils sont disponibles. Les manifestes précédents
sans ces champs restent lisibles.

## Onglet « Réimport · comparer » (aperçu WPF)

Dans l'assistant d'import DOCX, choisissez un **projet existant** dans l'onglet
Cible, puis ouvrez **Réimport · comparer** :

1. Cliquez sur **Rechercher les historiques**. Nodalis présente les imports
   dont le fichier a le même nom ou la même empreinte SHA-256, et signale
   si le nom correspond mais pas le contenu.
2. Sélectionnez **explicitement** l'historique Word à comparer.
3. Cliquez sur **Comparer** pour visualiser les sections anciennes et leur
   classification, y compris les correspondances incertaines.
4. Sélectionnez une section pour afficher côte à côte : **base du précédent
   import**, **nouveau Word** et **Markdown actuel dans Nodalis**.

La lecture vérifie que l'identifiant du projet correspond au manifeste et
que les destinations Markdown restent dans ce projet. Les fichiers absents
sont signalés ; les historiques corrompus ou les chemins dangereux sont
refusés. Les documents originaux et les notes actuelles ne sont pas modifiés.

**Limite de cette étape :** l'onglet ne propose volontairement **aucun bouton
d'application ou de fusion automatique**. Il prépare l'arbitrage sans prendre
de décision d'écriture. Une section affichée comme ambiguë n'est jamais
automatiquement considérée comme supprimée.

## Résolution manuelle et plan des décisions (#164, nouvel incrément)

Dans l'onglet **Réimport · comparer**, vous pouvez désormais sélectionner une
ancienne section, choisir **Associer la section sélectionnée** à une section
du nouveau Word, puis confirmer. Vous pouvez aussi indiquer explicitement
que la section a disparu du Word. Ces arbitrages sont conservés **uniquement
en mémoire** pendant cette comparaison. Changer d'historique les efface.

Quand le Word a été à la fois **renommé et modifié**, il ne correspond
plus nécessairement par nom ou empreinte SHA-256. Dans ce cas, le comparateur
présente les **autres historiques du projet** avec l'avertissement
« identité à confirmer » : cette liste sert uniquement au choix manuel et
ne vaut jamais preuve de correspondance.

Le moteur `DocxReimportMatchResolver` impose des identités de section uniques.
Une ancienne section ne peut pas être associée deux fois et deux anciennes
sections ne peuvent pas revendiquer une même section Word. Les index devenus
invalides sont refusés ; les correspondances ambiguës non arbitrées restent
signalées.

Le panneau repliable **Décisions de réimport · aperçu sans écriture** permet
de prévisualiser un choix individuel :

- **Conserver Nodalis** : aucun Markdown modifié ;
- **Utiliser Word** : proposition de remplacer uniquement le fragment
  précédemment importé, à condition de le retrouver exactement une fois ;
- **Fusion manuelle** : proposer un fragment Markdown relu et corrigé
  manuellement, avec la même contrainte d'ancrage ;
- **Garder comme historique** : le contenu Nodalis est préservé, y compris
  si le Word ne contient plus cette section.

Le plan refusé explique son motif : identités non résolues, fragment initial
introuvable ou présent plusieurs fois, destination absente, contenu vide ou
trop volumineux, ou plusieurs sections Word réunies dans le même Markdown.
Quand le fragment initial est localisé sans ambiguïté, le Markdown
**situé avant et après** est préservé par la proposition de remplacement.
Le plan conserve l'empreinte SHA-256 de la destination actuelle pour une
future vérification de concurrence avant commit. Les décisions ne sont pas
persistées ni appliquées.

**Attention :** le bouton d'import standard est désactivé tant que
l'onglet de réimport en lecture seule est actif. Un plan indiqué comme
« complet » n'est **pas** un commit : l'application des modifications,
le traitement des sections nouvelles et le rollback transactionnel restent
à implémenter avant que le réimport puisse écrire.

## Sécurité

Ce moteur n'enregistre **aucune décision**, n'écrase aucun Markdown et
n'efface aucun document. Les résultats indiquent quelles sections sont
éligibles à une proposition de mise à jour ou nécessitent un arbitrage.

**Restent à développer dans #164 :** diff visuel à la ligne/paragraphe,
traitement explicite des nouvelles sections, fusion assistée lorsque l'ancrage
initial a été modifié dans Nodalis, opérations idempotentes et commit de
réimport transactionnel. Cette étape ne constitue donc **pas** une fonctionnalité de
réimport prête à l'emploi.

La suite smoke vérifie les sept classifications, les conflits entre Word et
Nodalis et les destinations où plusieurs fragments ont été fusionnés.
