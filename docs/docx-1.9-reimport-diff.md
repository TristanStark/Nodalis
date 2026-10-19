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

## Sécurité

Ce moteur n'enregistre **aucune décision**, n'écrase aucun Markdown et
n'efface aucun document. Les résultats indiquent quelles sections sont
éligibles à une proposition de mise à jour ou nécessitent un arbitrage.

**Restent à développer dans #164 :** association sûre des sections anciennes
et nouvelles, aperçu différentiel par paragraphe, interface de résolution,
fusion manuelle, opérations idempotentes et commit de réimport
transactionnel. Cette étape ne constitue donc **pas** une fonctionnalité de
réimport prête à l'emploi.

La suite smoke vérifie les sept classifications, les conflits entre Word et
Nodalis et les destinations où plusieurs fragments ont été fusionnés.
