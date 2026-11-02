# Nodalis 2.0 — Migration guidée des projets Complexes (#194)

## Périmètre actuel

Le moteur de migration traite les liens Markdown internes et les backlinks
explicites d'autres projets, à condition que chaque remplacement soit prouvé
par empreinte, sauvegardé et accepté par l'utilisateur. Un projet existant n'est jamais migré à l'ouverture, ni par la
simple consultation d'un inventaire. Les projets Simple, Moyen et Ultra-simple
ne sont pas migrables avec ce service.

## Étapes dans Nodalis

1. Enregistrer et fermer tous les onglets du projet et de ses sous-projets.
2. Sélectionner le projet Complexe dans le worktree.
3. Ouvrir « Migrer un projet Complexe vers la structure 2.0 » dans la palette.
4. Examiner les mouvements, les obstacles et l'empreinte SHA-256.
5. Si le plan est éligible, saisir précisément MIGRER, puis confirmer.
6. Conserver le chemin de sauvegarde et du journal affichés après publication.

La commande « Restaurer un projet Complexe avant migration » demande une
confirmation distincte. Elle ne rétablit l'ancien projet que si les deux
versions correspondent encore aux empreintes du journal.

## Préflight sans écriture

Le service contrôle le manifeste Complexe, son statut non archivé, les GUID
stables, collisions des chemins, sections personnalisées, sources absentes,
jonctions / liens symboliques et dossiers parents.

Il calcule une empreinte SHA-256 regroupant tous les noms, les dossiers
vides et les hashes du contenu de tous les fichiers du projet.

Le préflight identifie et prévisualise les chemins relatifs Markdown (y
compris images) et les attributs HTML cités qui changeraient de destination.
Il propose les remplacements exacts pour les documents **du projet migré**
et les backlinks **dans d'autres projets**, avec positions de texte,
empreintes SHA-256 et conservation des ancres et fins de ligne
(UTF-8 avec ou sans BOM).

Les liens externes, ancres et références qui restent valides ne sont pas
modifiés. Les références Markdown **explicites et simples** `[texte][cle]`
avec une définition unique `[cle]: chemin` (éventuellement encadré de
`<...>` et suivi d'un titre entre guillemets) sont prises en charge :
seule la cible de la **définition** est réécrite si le chemin change.
Les images utilisant cette forme et les backlinks extérieurs sont soumis
aux mêmes vérifications et sauvegardes transactionnelles.
Les définitions dupliquées, les usages sans définition et les variantes
complexes restent bloquants. Une syntaxe wiki, un chemin encodé/ambigu
ou une source non UTF-8 restent également bloquants.
Les blocs de code clôturés sont exclus ; les lignes contenant du code inline
et des liens sont refusées plutôt que réécrites. Templates et journaux
techniques sont hors périmètre de l'analyse.

## Sauvegarde, journal et transaction

Une transaction approuvée conserve les données dans :

    Imports/.complex-v2-migrations/<uuid>/
      journal.json
      original/
      staging/                  si publication non achevée
      previously-published/     après restauration
      external-original/        sauvegardes exactes des backlinks externes
      external-staging/         backlinks corrigés, prêts à publication

Le projet est copié en staging. Les références locales approuvées sont
réécrites **uniquement dans cette copie**, après contrôle du SHA-256 de chaque
fichier et des offsets exacts. Ensuite les sections physiques sont déplacées
dans cette copie seulement, les identifiants de sections existantes sont
conservés et les nouveaux conteneurs obtiennent de nouveaux GUID.

Avant publication, l'index partagé est actualisé. La transaction capture pour
chaque document Markdown déplacé son GUID, ses chemins source/cible et les
empreintes de ses octets **avant/après** réécriture. Cette provenance est
enregistrée dans `journal.json`, avant toute bascule. Lors du prochain
rafraîchissement, le même index réutilise le GUID original même lorsque le
chemin **et** le contenu ont changé ensemble ; les relations et blocs sont
reprojetés sous cet identifiant. Les journaux terminés sont pris en compte
aussi après un redémarrage ; les restaurations approuvées renversent la
direction du mapping. Une collision de GUID, un changement d'empreinte avant
la première réconciliation ou une entrée de journal invalide est refusé.

Chaque fichier externe possède ses deux copies, son chemin contrôlé et les
SHA-256 d'origine et de publication dans le journal **avant toute mutation**.
Avant de déplacer le projet, Nodalis revérifie **tous** les backlinks
externes ; si un fichier a été modifié par un autre éditeur depuis la
préparation, l'opération est refusée sans déplacer le projet et le journal
est classé `aborted`. Avant chaque reprise, **tout l'inventaire externe**
est aussi vérifié en une passe avant le premier remplacement (puis chaque
fichier est revérifié individuellement). Cela réduit les publications
partielles évitables, sans prétendre verrouiller les logiciels tiers.
Les onglets externes concernés doivent être fermés. Le journal de transaction
est écrit AVANT toute bascule :

1. Original du projet vers `original/` dans le dossier de transaction.
2. Revérification de l'original conservé.
3. Staging vers l'emplacement initial du projet.
4. Remplacement atomique individuel des backlinks externes, seulement si leur
   empreinte correspond encore exactement à l'une des deux versions approuvées.
5. Vérification des empreintes du projet et fin de transaction.

Un arrêt entre deux remplacements externes laisse la transaction **en attente**.
Une récupération explicite vérifie puis termine les remplacements restant à
appliquer ; un changement humain depuis la migration fait refuser le replay.
Une restauration approuvée rétablit aussi les octets exacts de chaque backlink.
Les deux copies externes sont conservées avec l'original du projet.

L'original est conservé sans purge automatique. En cas de crash, Nodalis
demande explicitement une récupération au démarrage avant d'ouvrir le
workspace en écriture. Un projet concurrent ou modifié n'est jamais
écrasé automatiquement. La restauration d'un projet migré garde aussi
la version publiée dans previously-published/.

## Limites et qualification

- Les backlinks dans d'autres projets sont désormais réécrits avec journal et
  récupération contrôlée. Le passage entre plusieurs fichiers externes n'est
  pas une opération système atomique unique : en cas d'interruption, Nodalis
  bloque les nouvelles migrations jusqu'à récupération explicitement approuvée.
- Les syntaxes Markdown/HTML ambiguës (dont les références indirectes
  non explicites ou non résolues) et les fichiers non UTF-8 exigent une
  requalification ; aucun remplacement deviné.
- Une copie intégrale du projet nécessite un espace disque suffisant.
- Les backlinks sont désormais remplacés par une écriture conditionnelle :
  copie temporaire sur le même volume vérifiée par SHA-256, puis relecture du
  document vivant sous un handle `FileShare.Read | FileShare.Delete` conservé
  jusqu'à `File.Replace`. Sur Windows, ce handle exclut les écritures directes
  concurrentes pendant la dernière comparaison et le remplacement.
- Sur Windows, après le dernier contrôle et avant `File.Replace`, Nodalis
  compare également l'identité native (volume + index de fichier) du handle
  verrouillé avec celle d'un **nouveau handle ouvert par le chemin**. Un éditeur
  ayant substitué un autre fichier, même avec des octets identiques, provoque
  un refus et la récupération reste explicite. Un test injecte ce déplacement
  suivi d'une copie conforme sous le même nom.
- Un logiciel tiers peut encore remplacer atomiquement le chemin **après**
  ce contrôle d'identité et avant `File.Replace`. La méthode réduit cette
  fenêtre sans devenir une transaction globale entre éditeurs ; le contrôle
  de hash après publication et le journal restent nécessaires.
- Sauvegardes et journaux restent disponibles jusqu'à suppression explicite.
- La qualification visuelle WPF sous Windows reste nécessaire avant release.

Tests automatiques : approbation obligatoire, plan périmé, protection des
liens, GUID stables, sauvegarde exacte, interruption avant/après publication,
récupération, refus de rollback après édition et restauration interrompue.
La suite `ComplexMigrationHardKillSmokeTests` déclenche de vraies fins de
processus Windows après publication du premier backlink, après déplacement
de la source lors du rollback et après restauration du premier backlink.
Chaque cas réouvre le workspace depuis le processus parent et exige une
récupération explicitement approuvée, puis vérifie les octets et chemins
attendus ainsi que l'absence de journal en attente.

## Références Markdown qualifiées — 31/10/2026

Le préflight distingue désormais les définitions `[cle]: <chemin>` des
usages explicites `[libellé][cle]` et `![image][cle]`. Une clé simple
ASCII, une seule définition par document (sans ambiguïté de casse), un
chemin littéral et, facultativement, un titre entre guillemets sont
requis. Le contrôle s'effectue hors blocs de code clôturés et refuse
les références inconnues ou ambiguës. Le plan indique les offsets
exacts de la cible dans le document d'origine, puis les rejoue dans
le staging ou les snapshots des backlinks externes, comme pour les
liens Markdown directs.

Tests de régression : liens internes, image, lien externe contenant des
espaces dans le chemin entre chevrons, publication, rollback octet par
octet, définition dupliquée et référence manquante. Les références
wiki `[[...]]`, les syntaxes abrégées complexes et les adresses encodées
ne sont pas interprétées automatiquement.


## Périmètre de qualification des backlinks — novembre 2026

Le précontrôle de migration ne doit pas considérer les documents d'autres
applications comme s'ils faisaient partie du projet migré. Les liens internes
au projet cible restent inspectés de manière stricte. Hors du projet :

- les backlinks **valides** vers une destination réellement déplacée sont
  toujours proposés en réécriture et protégés dans la transaction/rollback ;
- une syntaxe ambiguë ou non prise en charge ne bloque que si son texte peut
  concerner un chemin déplacé de ce projet ;
- les liens cassés, références Markdown indéfinies, fichiers non UTF-8 et
  jonctions **sans rapport** ne bloquent plus cette migration ;
- un lien externe suspect vers un chemin effectivement déplacé reste bloquant
  lorsqu'il ne peut pas être réécrit sans ambiguïté.

Ce filtrage n'autorise pas à supprimer ou éditer des backlinks non qualifiés.
Le journal de transaction continue de vérifier les instantanés et empreintes
des fichiers effectivement réécrits, avec contrôle des changements concurrents.

Recette : projet cible dans Application A, Markdown malformé dans Application B,
backlink réel dans Application B, source UTF-8 et source non UTF-8, absence
d'écriture pendant l'aperçu et rollback complet après exécution.


## Préflight incrémental de périmètre (02/11/2026)

Le contrôle des liens externes effectue une présélection textuelle avant
l'analyse des syntaxes Markdown/HTML. Pour qu'une source **extérieure** au
projet cible pointe vers un fichier déplacé à l'intérieur de ce projet, sa
référence doit passer par le nom de dossier du projet (éventuellement encodé
comme une URL). Les fichiers ne mentionnant pas ce dossier ne requièrent
aucune analyse de liens. À l'intérieur du projet, l'inspection demeure
exhaustive, y compris lorsque le Markdown est incomplet ou mal formé.

Les fragments de lien nécessitant une requalification humaine continuent
d'être bloquants. Les backlinks externes vérifiés restent dans le journal et
conservent leur SHA-256 ; l'empreinte d'un Markdown est calculée uniquement
lorsqu'une réécriture est effectivement proposée. Le panneau de confirmation
affiche désormais les nombres de documents du projet inspectés, de sources
externes candidates et de sources externes écartées sans analyse. Chaque
blocage pertinent est affiché sur une ligne distincte, plutôt qu'une longue
liste concaténée et tronquée.

Les tests utilisent un workspace fictif comprenant plusieurs applications et
une série de trente documents externes sans rapport, ainsi qu'un backlink
suspect et un backlink réécrivable. Le préflight reste **en lecture seule** ;
la qualification visuelle sur Windows et la mesure des latences avec un
workspace volumineux sont requises avant publication 2.0.
