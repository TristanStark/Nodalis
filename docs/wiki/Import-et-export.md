# Importer et exporter des documents

Nodalis privilégie des formats portables et des opérations **prévisualisées avant écriture**.

## Import DOCX

Le parcours documenté est le suivant :

1. Sélectionner un fichier `.docx`.
2. Nodalis travaille sur une **copie locale**, sans modifier l'original.
3. Examiner les sections et paragraphes détectés.
4. Sélectionner les parties utiles, ainsi que le projet ou l'application de destination.
5. Contrôler le remapping des sections, l'aperçu Markdown et le **plan exact des fichiers**.
6. Valider explicitement l'import.

Cela permet d'éviter de transformer involontairement un document entier en une multitude de fichiers.

Pour la validation de la v1.9 sur `develop` :
[grille manuelle DOCX](https://github.com/TristanStark/Nodalis/blob/develop/docs/docx-1.9-manual-qualification.md).

## Import Markdown en masse

L'analyse préalable peut présenter les documents à importer, repérer les **pièces jointes relatives**, identifier des doublons et proposer un plan avant validation.

Les pièces jointes et sources restent inspectables comme des fichiers ordinaires. Les sources importées peuvent être conservées dans `Imports/Sources/` pour audit.

## Exporter un projet

Le projet peut être exporté en :

- **Markdown portable** ;
- **HTML autonome hors ligne** ;
- **DOCX natif**.

L'export ne modifie pas le projet source et tient compte de l'ordre des sections et des pièces jointes compatibles avec le format choisi.

## En cas de problème

Vérifiez que le DOCX est valide et non verrouillé par Word, puis reprenez le plan d'import et la destination. Un fichier `.doc` ancien format n'est pas équivalent à un `.docx`.

**Référence :** [Import/export — guide détaillé](https://github.com/TristanStark/Nodalis/blob/develop/docs/import-export.md).
