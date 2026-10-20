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

Depuis la v1.9 (développement sur `develop`), chaque import avancé validé
conserve aussi un manifeste de provenance local (`.nodalis-imports/*.json`)
dans le projet : origine SHA-256, sélection, destinations et snapshots.
Voir [DOCX v1.9 — Traçabilité des imports](docx-1.9-provenance.md).
Le réimport différentiel et l'arbitrage des conflits sont prévus séparément (#164).

### Protection des documents Word en v1.9 (#165, en développement)

Avant toute interprétation, l'import DOCX valide l'archive ZIP : chemins
relatifs, unicité des entrées, absence de liens symboliques, limites sur
le nombre de fichiers, les tailles décompressées et les compressions
anormalement élevées. Les parties XML sont lues sans DTD, résolution
d'entités externes ni téléchargement distant.

**Limites de sécurité par document** : 4 096 entrées ZIP, 256 Mio de
contenu décompressé au total, 32 Mio par partie XML, 16 Mio par média,
64 Mio par autre entrée ; taux de compression supérieur à 1 000 pour
une entrée de plus de 1 Mio refusé. Ces limites protègent le poste de
travail : un DOCX volontairement très volumineux peut être refusé sans
que son contenu soit endommagé. Les médias sont revérifiés au moment
de la copie et aucune pièce jointe n'est créée pour un paquet invalide.

Le SHA-256 de la copie temporaire est enregistré **au moment de l'analyse** ;
un plan ou commit ultérieur est refusé si les octets de cette copie ont changé.
La copie est limitée à 128 Mio, y compris si le fichier source grandit pendant
la copie. Les nouveaux Markdown sont créés atomiquement en mode **création
exclusive** : un fichier apparu entre prévisualisation et import ne peut
jamais être écrasé.

Lors d'une erreur d'import dans un projet existant, les octets du manifeste
projet sont restaurés **uniquement si le fichier contient encore la version
écrite par cet import**. Une modification externe concurrente est conservée,
pas écrasée par le rollback. La restauration du manifeste est validée par
un test d'interruption et un test d'édition concurrente.

Cette protection ne remplace pas les garanties transactionnelles et
la récupération sur interruption qui restent à qualifier dans #165.

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
