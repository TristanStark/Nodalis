# Nodalis 2.0 — Migration guidée des projets Complexes (#194)

## Périmètre actuel

Ce moteur exécutable est limité aux projets sans référence Markdown à
réécrire. Les liens externes et les chemins relatifs dont la résolution reste
identique après déplacement sont désormais autorisés. Un projet existant n'est jamais migré à l'ouverture, ni par la
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
Il propose des remplacements exacts seulement pour les documents **situés
dans le projet migré**, avec positions de texte, empreintes SHA-256 et
conservation des ancres et fins de ligne (UTF-8 avec ou sans BOM).

Les liens externes, ancres et références qui restent valides ne sont pas
modifiés. Toute référence affectée **depuis un autre projet**, une syntaxe
wiki/indirecte, un chemin encodé/ambigu ou une source non UTF-8 reste bloquante.
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

Après une nouvelle vérification de l'empreinte originale, le journal est
écrit AVANT les deux renommages de répertoires sur le même volume :

1. Original vers original/ dans le dossier de transaction.
2. Revérification de l'original conservé.
3. Staging vers l'emplacement initial du projet.
4. Vérification de l'empreinte publiée et journal terminé.

L'original est conservé sans purge automatique. En cas de crash, Nodalis
demande explicitement une récupération au démarrage avant d'ouvrir le
workspace en écriture. Un projet concurrent ou modifié n'est jamais
écrasé automatiquement. La restauration d'un projet migré garde aussi
la version publiée dans previously-published/.

## Limites et qualification

- Les références nécessitant une réécriture **hors du projet** bloquent
  encore l'exécution ; aucun demi-déplacement inter-projets n'est permis.
- Les syntaxes Markdown/HTML ambiguës ou les fichiers non UTF-8 exigent
  une requalification ; aucun remplacement deviné.
- Une copie intégrale du projet nécessite un espace disque suffisant.
- Un autre logiciel peut encore modifier les fichiers en parallèle ;
  les vérifications d'empreintes et le verrou du moteur limitent ce risque,
  sans offrir un verrou global sur les logiciels externes.
- Sauvegardes et journaux restent disponibles jusqu'à suppression explicite.
- La qualification visuelle WPF sous Windows reste nécessaire avant release.

Tests automatiques : approbation obligatoire, plan périmé, protection des
liens, GUID stables, sauvegarde exacte, interruption avant/après publication,
récupération, refus de rollback après édition et restauration interrompue.
