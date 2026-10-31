# DOCX v1.9 — Réimport différentiel et application explicitement confirmée (#164)

## État du développement

L'issue #164 dispose d'un comparateur **à trois voies en lecture seule**,
d'un rapprochement des sections Word, d'arbitrages manuels, d'un différentiel
par paragraphe et d'un plan de remplacement sécurisé. La commande
**Préparer le réimport** revalide l'historique et les destinations actuelles ;
une seconde commande **Appliquer le plan confirmé** ne déclenche les écritures
qu'après validation Yes/No du détail des fichiers. Le système conserve
une copie Word et un manifeste versionné, journalise les sauvegardes
avant modification et restaure les fichiers si nécessaire.

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

Après un réimport avec **fusion manuelle** ou **conservation de Nodalis**,
l'appariement des sections et la détection des changements Word utilisent le
dernier snapshot Word réellement observé (avec son SHA-256), et **non** le
texte Nodalis accepté. Cela évite de détecter à tort le même document Word
comme une nouvelle modification à chaque génération.

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
refusés. La **comparaison seule** ne modifie ni les documents originaux ni les notes.
Une section ambiguë n'est jamais automatiquement considérée comme supprimée.
Seules les sections résolues et explicitement acceptées peuvent être
appliquées après préparation puis confirmation séparée.

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
- **Fusion manuelle · remplacer le fichier entier** : réservée aux fichiers
  Nodalis qui ne contiennent **qu'un seul fragment issu de cet historique**.
  Cliquez d'abord sur **Charger le Markdown Nodalis actuel** pour partir de
  toutes les notes existantes, puis comparez le Word récent, modifiez le texte
  complet et cochez séparément **Je confirme remplacer le Markdown COMPLET**.
  Sans cette deuxième confirmation, le plan refuse toute écriture.
- **Garder comme historique** : le contenu Nodalis est préservé, y compris
  si le Word ne contient plus cette section.

Le plan refusé explique son motif : identités non résolues, fragment initial
introuvable ou présent plusieurs fois, destination absente, contenu vide ou
trop volumineux, ou plusieurs sections Word réunies dans le même Markdown.
Quand le fragment initial est localisé sans ambiguïté, le Markdown
**situé avant et après** est préservé par la proposition de remplacement.
Si cet ancrage a été modifié dans Nodalis, le remplacement ordinaire reste
**interdit**. Le mode « fichier entier » écrit **exactement** le contenu
fusionné par l'utilisateur, sans tentative de deviner quels paragraphes
conserver. Les notes absentes du texte final seraient perdues : l'interface
affiche un avertissement spécifique jusque dans la confirmation finale.
Le hash du **Markdown complet courant** est recalculé à la préparation,
puis vérifié une nouvelle fois immédiatement avant l'écriture ; toute
modification concurrente annule le réimport. Les copies de sauvegarde sont
conservées dans le journal de transaction.
Le plan conserve l'empreinte SHA-256 de la destination actuelle pour une
future vérification de concurrence avant commit. Les décisions ne sont pas
persistées tant que le réimport n'a pas été explicitement confirmé.

**Attention :** le bouton d'import standard reste désactivé dans l'onglet
de réimport pour empêcher toute confusion. Un plan indiqué comme « complet »
ne déclenche **aucune écriture** : vous devez encore cliquer sur
**Préparer le réimport**, relire l'aperçu de ses écritures puis
**Appliquer le plan confirmé** avec une confirmation positive.
L'approbation est invalidée au moindre changement des décisions ou
du document depuis l'aperçu. Voir aussi
[transactions et récupération](docx-1.9-reimport-transaction.md).

## Différentiel visuel des paragraphes

Le panneau **Différentiel par paragraphe · lecture seule** affiche deux
comparaisons indépendantes : **base ↔ nouveau Word** et
**base ↔ Markdown Nodalis**. Les lignes portent les marqueurs
`=` inchangé, `+` ajouté, `−` supprimé et `±` modifié, ainsi que les
numéros de ligne des blocs dans chaque version. Le survol révèle le texte
complet d'origine et d'arrivée.

Le moteur `DocxReimportParagraphDiffEngine` utilise un appariement
déterministe par plus longue sous-séquence commune de paragraphes. Les
fins de ligne LF/CRLF sont neutralisées **uniquement pour la présentation**,
sans toucher aux fichiers ; les lignes vides au sein des blocs Markdown
`~~~` ou `\`\`\`` sont conservées ensemble. Il ne s'agit pas d'une
fusion automatique ni d'un diff sémantique.

Si le fragment d'origine est localisable **une seule fois** dans le fichier
Nodalis, les notes situées hors de ce fragment ne sont pas comptabilisées
comme des modifications de celui-ci. Sinon, le panneau indique qu'il compare
le **fichier Markdown entier**, faute de délimiteur sûr. Les comparaisons
au-delà de 300 paragraphes ou 256 000 caractères par côté affichent
un avertissement de limitation plutôt que de bloquer la fenêtre.

## Nouvelles sections Word : importer ou exclure

Le panneau **Nouvelles sections Word · décision explicite** affiche les
sections sans correspondance avec l'ancien import. Vous pouvez maintenant :

- **Importer dans ce dossier** : sélectionner un dossier **déjà existant**
  du projet (racine ou dossier de premier niveau), puis affecter la nouvelle
  section à un fichier Markdown créé uniquement après confirmation. Nodalis
  propose un fichier de nom déterministe
  `DOCX-reimport-<import-parent-GUID>-section-<index>.md` ;
  ce fichier n'est **jamais écrasé** s'il existe déjà.
- **Exclure de ce réimport** : laisser explicitement la nouvelle section
  hors de cette opération, sans création de fichier.

La liste des dossiers exclut les dossiers internes commençant par un point
et les liens symboliques / jonctions. Un dossier choisi qui disparaît, une
destination déjà occupée ou une décision doublonnée bloquent l'application.
Les sections non traitées empêchent `ReviewComplete` et les correspondances
ambiguës ne peuvent pas être dissimulées par ce mécanisme.

Les fichiers nouveaux sont suivis dans le journal `Prepared` **avant leur
création**, avec leur empreinte SHA-256. Ils apparaissent dans le manifeste
de provenance au même titre que les sections anciennes, conservent leur
identifiant de fragment et participent à la reprise et au rollback. Les
fichiers nouveaux sont retirés lors d'une restauration uniquement s'ils
n'ont pas été modifiés par une opération extérieure.

**Limite actuelle** : chaque nouvelle section occupe un fichier Markdown
dédié, dans un dossier existant. L'ajout au sein d'un Markdown préexistant
ou la création automatique d'une nouvelle catégorie ne sont pas proposés :
ces opérations nécessitent des règles de fusion plus avancées.

## Sécurité

Le comparateur et les arbitrages ne touchent jamais les fichiers. Le
parcours d'application dispose d'un journal transactionnel, de sauvegardes
binaires et d'un manifeste immuable. Les plans périmés sont refusés.

Les nouveaux imports enregistrent désormais
`originalSourceBlockCount`, le nombre total de blocs disponibles dans la
section Word initiale. Si l'utilisateur n'en avait importé qu'une sélection,
le réimport est **bloqué avec un avertissement explicite** : on ne
réintroduit pas automatiquement les paragraphes autrefois exclus. Les
anciens manifestes sans ce champ demeurent lisibles.

**Restent à qualifier dans #164 :** le comportement WPF/Windows des scénarios
extrêmes de concurrence, permissions et récupération, ainsi que la
gestion de documents partagés par plusieurs fragments et la création de
nouvelles catégories. Le mode de fusion de document complet exige toujours
un arbitrage humain et ne constitue pas une fusion automatique.

La suite smoke vérifie les sept classifications, les conflits entre Word et
Nodalis et les destinations où plusieurs fragments ont été fusionnés.
