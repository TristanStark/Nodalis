# Import et export Nodalis 1.0

Nodalis conserve les données localement et ne modifie jamais une source externe
en place pendant un import.

## Import DOCX

L'import DOCX suit une séquence de prévisualisation avant toute écriture métier :

1. le fichier source est copié dans la zone locale Imports/Sources ;
2. Nodalis analyse la structure du document ;
3. les paragraphes et sections importables sont présentés ;
4. la destination Application / Projet et le remapping des sections sont définis ;
5. un aperçu Markdown et le plan exact des fichiers à créer sont affichés ;
6. les fichiers ne sont écrits qu'après validation.

La sélection fine permet de ne retenir qu'une partie des sections ou blocs du
document.

Le fichier DOCX original reste inchangé, y compris en cas d'échec.

## Import Markdown en masse

L'import Markdown en masse prépare un plan avant validation.

Il peut notamment :

- prévisualiser les documents détectés ;
- conserver le Markdown lisible ;
- découvrir les pièces jointes référencées par chemin relatif ;
- détecter les doublons déjà importés ;
- appliquer ensuite le plan explicitement validé.

La détection de doublons empêche de réimporter silencieusement le même contenu.

## Pièces jointes et sources importées

Les pièces jointes restent des fichiers ordinaires. Les sources importées sont
conservées dans le workspace afin que l'utilisateur puisse auditer l'origine des
documents générés.

Les chemins restent relatifs au workspace lorsque cela est possible.

## Export de projet

L'export de projet produit une copie de diffusion sans modifier le projet source.

Les formats couverts par la suite de régression 1.0 sont :

- Markdown portable ;
- HTML autonome utilisable hors ligne ;
- DOCX natif.

L'export respecte l'ordre des sections et prend en compte les pièces jointes
compatibles avec le format choisi.

## Garanties

- aucun accès réseau requis ;
- aucune dépendance à un service cloud ;
- la source n'est pas modifiée par l'export ;
- un import présente son plan avant écriture ;
- les erreurs d'import ne doivent pas altérer le document d'origine ;
- les données générées restent inspectables hors de Nodalis.

## Dépannage

Si un DOCX ne peut pas être importé :

1. vérifiez qu'il s'agit bien d'un fichier DOCX valide et non d'un ancien DOC ;
2. fermez le document dans les applications susceptibles de le verrouiller ;
3. relancez l'analyse et vérifiez les blocs sélectionnés ;
4. confirmez qu'une destination de projet valide existe ;
5. consultez troubleshooting.md si l'échec persiste.

Pour un export, vérifiez d'abord que le projet et ses documents sont lisibles
normalement dans Nodalis. Une erreur d'export ne doit pas modifier le projet.
